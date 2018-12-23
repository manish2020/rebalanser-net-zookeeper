using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using org.apache.zookeeper;
using Rebalanser.Core.Logging;

namespace Rebalanser.ZooKeeper.Zk
{
    public class ZooKeeperService : Watcher, IZooKeeperService
    {
        private org.apache.zookeeper.ZooKeeper zookeeper;
        private ILogger logger;
        private string zookeeperHosts;
        private string clientsPath;
        private string statusPath;
        private string stoppedPath;
        private string resourcesPath;
        private string epochPath;
        private Event.KeeperState keeperState;
        private string clientId;

        public ZooKeeperService(string zookeeperHosts, ILogger logger)
        {
            this.zookeeperHosts = zookeeperHosts;
            this.logger = logger;
            this.clientId = "Pending Id";
        }
        
        public async Task<bool> InitializeGlobalBarrierAsync(string clientsPath,
            string statusPath,
            string stoppedPath,
            string resourcesPath,
            string epochPath)
        {
            this.clientsPath = clientsPath;
            this.statusPath = statusPath;
            this.stoppedPath = stoppedPath;
            this.resourcesPath = resourcesPath;
            this.epochPath = epochPath;

            var clientsPathRes = await EnsurePathAsync(this.clientsPath);
            if (clientsPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.clientsPath} does not exist and could not be created");
                return false;
            }
            
            var epochPathRes = await EnsurePathAsync(this.epochPath);
            if (epochPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.epochPath} does not exist and could not be created");
                return false;
            }
            
            var statusPathRes = await EnsurePathAsync(this.statusPath);
            if (statusPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.statusPath} does not exist and could not be created");
                return false;
            }
            
            var stoppedPathRes = await EnsurePathAsync(this.stoppedPath);
            if (stoppedPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.stoppedPath} does not exist and could not be created");
                return false;
            }
            
            var resourcesPathRes = await EnsurePathAsync(this.resourcesPath);
            if (resourcesPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.resourcesPath} does not exist and could not be created");
                return false;
            }

            return true;
        }
        
        public async Task<bool> InitializeResourceBarrierAsync(string clientsPath,
            string resourcesPath,
            string epochPath)
        {
            this.clientsPath = clientsPath;
            this.resourcesPath = resourcesPath;
            this.epochPath = epochPath;

            var clientsPathRes = await EnsurePathAsync(this.clientsPath);
            if (clientsPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.clientsPath} does not exist and could not be created");
                return false;
            }
            
            var epochPathRes = await EnsurePathAsync(this.epochPath);
            if (epochPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.epochPath} does not exist and could not be created");
                return false;
            }
            
            var resourcesPathRes = await EnsurePathAsync(this.resourcesPath);
            if (resourcesPathRes != ZkResult.Ok)
            {
                this.logger.Error(this.clientId, $"znode {this.resourcesPath} does not exist and could not be created");
                return false;
            }

            return true;
        }

        public Event.KeeperState GetKeeperState()
        {
            return this.keeperState;
        }

        public async Task<bool> StartSessionAsync(TimeSpan sessionTimeout, TimeSpan connectTimeout)
        {
            var sw = new Stopwatch();
            sw.Start();
            
            if (this.zookeeper != null)
                await this.zookeeper.closeAsync();

            this.zookeeper = new org.apache.zookeeper.ZooKeeper(
                this.zookeeperHosts, 
                (int)sessionTimeout.TotalMilliseconds,
                this);

            while (this.keeperState != Event.KeeperState.SyncConnected && sw.Elapsed <= connectTimeout)
                await Task.Delay(50);

            return this.keeperState == Event.KeeperState.SyncConnected;
        }

        public async Task CloseSessionAsync()
        {
            await this.zookeeper.closeAsync();
            this.zookeeper = null;
        }

        public override async Task process(WatchedEvent @event)
        {
            this.keeperState = @event.getState();
            await Task.Yield();
        }

        public async Task<ZkResponse<string>> CreateClientAsync()
        {
            try
            {
                var clientPath = await this.zookeeper.createAsync(
                    $"{this.clientsPath}/client_",
                    System.Text.Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL_SEQUENTIAL);

                this.clientId = clientPath.Substring(clientPath.LastIndexOf("/")+1);

                return new ZkResponse<string>(ZkResult.Ok, clientPath);
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not create client znode as parent node does not exist: " + e);
                return new ZkResponse<string>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not create client znode as the connection has been lost: " + e);
                return new ZkResponse<string>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not create client znode as the session has expired: " + e);
                return new ZkResponse<string>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not create client znode: " + e);
                return new ZkResponse<string>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResult> DeleteClientAsync(string clientPath)
        {
            try
            {
                await this.zookeeper.deleteAsync(clientPath);
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not delete client znode as the node does not exist: " + e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not delete client znode as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not delete client znode as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not create client znode: " + e);
                return ZkResult.UnexpectedError;
            }
        }

        public async Task<ZkResult> EnsurePathAsync(string znodePath)
        {
            try
            {
                var znodeStat = await this.zookeeper.existsAsync(znodePath);
                if (znodeStat == null)
                {
                    await this.zookeeper.createAsync(znodePath,
                        System.Text.Encoding.UTF8.GetBytes("0"),
                        ZooDefs.Ids.OPEN_ACL_UNSAFE,
                        CreateMode.PERSISTENT);
                }

                return ZkResult.Ok;
            }
            catch (KeeperException.NodeExistsException)
            {
                return ZkResult.Ok;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, $"Could not create znode {znodePath} as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, $"Could not create znode {znodePath} as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, $"Could not create znode {znodePath}: " + e);
                return ZkResult.UnexpectedError;
            }
            //
        }
        
        public async Task<ZkResponse<int>> IncrementEpochAsync(int currentEpoch)
        {
            try
            {
                var data = System.Text.Encoding.UTF8.GetBytes("0");
                var stat = await zookeeper.setDataAsync(this.epochPath, data, currentEpoch);
                return new ZkResponse<int>(ZkResult.Ok, stat.getVersion());
            }
            catch (KeeperException.BadVersionException e)
            {
                this.logger.Error(this.clientId, "Could not increment epoch as the current epoch was incremented already. Stale epoch: " + e);
                return new ZkResponse<int>(ZkResult.BadVersion);
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not increment epoch as the node does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not increment epoch as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not increment epoch as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not increment epoch: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }
        
        public async Task<ZkResponse<int>> GetEpochAsync()
        {
            try
            {
                var dataResult = await zookeeper.getDataAsync(this.epochPath);
                return new ZkResponse<int>(ZkResult.Ok, dataResult.Stat.getVersion());
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not get the current epoch as the node does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not get the current epoch as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not get the current epoch as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not get the current epoch: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResponse<ClientsZnode>> GetActiveClientsAsync()
        {
            try
            {
                var childrenResult = await this.zookeeper.getChildrenAsync(this.clientsPath);
                var childrenPaths = childrenResult.Children.Select(x => $"{this.clientsPath}/{x}").ToList();
                return new ZkResponse<ClientsZnode>(ZkResult.Ok, new ClientsZnode()
                {
                    Version = childrenResult.Stat.getVersion(),
                    ClientPaths = childrenPaths
                });
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not get children as the node does not exist: " + e);
                return new ZkResponse<ClientsZnode>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not get children as the connection has been lost: " + e);
                return new ZkResponse<ClientsZnode>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not get children epoch as the session has expired: " + e);
                return new ZkResponse<ClientsZnode>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not get children: " + e);
                return new ZkResponse<ClientsZnode>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResponse<StatusZnode>> GetStatusAsync()
        {
            try
            {
                var dataResult = await zookeeper.getDataAsync(this.statusPath);
                var status = RebalancingStatus.NotSet;
                if (dataResult.Stat.getDataLength() > 0)
                    status = (RebalancingStatus) BitConverter.ToInt32(dataResult.Data, 0);
                        
                return new ZkResponse<StatusZnode>(ZkResult.Ok, new StatusZnode()
                {
                    RebalancingStatus = status,
                    Version = dataResult.Stat.getVersion()
                });
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not get status as the node does not exist: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not get status as the connection has been lost: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not get status epoch as the session has expired: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not get status: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResponse<int>> SetStatus(StatusZnode statusZnode)
        {
            try
            {
                var data = BitConverter.GetBytes((int) statusZnode.RebalancingStatus);
                var stat = await zookeeper.setDataAsync(this.statusPath, data, statusZnode.Version);
                return new ZkResponse<int>(ZkResult.Ok, stat.getVersion());
            }
            catch (KeeperException.BadVersionException e)
            {
                this.logger.Error(this.clientId, "Could not set status due to a bad version number. " + e);
                return new ZkResponse<int>(ZkResult.BadVersion);
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set status as the node does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set status as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set status as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set status: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResult> SetFollowerAsStopped(string clientId)
        {
            try
            {
                await this.zookeeper.createAsync(
                    $"{this.stoppedPath}/{clientId}",
                    System.Text.Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL);
                return ZkResult.Ok;
            }
            catch (KeeperException.NodeExistsException)
            {
                return ZkResult.NodeAlreadyExists;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not add follower to the stopped list as the stopped node does not exist: " +
                                  e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not add follower to the stopped list as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not add follower to the stopped list as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not add follower to the stopped list: " + e);
                return ZkResult.UnexpectedError;
            }
        }
        
        public async Task<ZkResult> SetFollowerAsStarted(string clientId)
        {
            try
            {
                await this.zookeeper.deleteAsync($"{this.stoppedPath}/{clientId}");
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException)
            {
                return ZkResult.Ok;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not remove follower from the stopped list as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not remove follower from the stopped list as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not remove follower from the stopped list: " + e);
                return ZkResult.UnexpectedError;
            }
        }

        public async Task<ZkResponse<ResourcesZnode>> GetResourcesAsync()
        {
            try
            {
                var dataResult = await this.zookeeper.getDataAsync(this.resourcesPath);
                var childrenResult = await this.zookeeper.getChildrenAsync(this.resourcesPath);
                var resourcesZnodeData = JsonConvert.DeserializeObject<ResourcesZnodeData>(
                        System.Text.Encoding.UTF8.GetString(dataResult.Data));
                
                if(resourcesZnodeData == null)
                    resourcesZnodeData = new ResourcesZnodeData();

                return new ZkResponse<ResourcesZnode>(ZkResult.Ok, new ResourcesZnode()
                {
                    ResourceAssignments = resourcesZnodeData,
                    Resources = childrenResult.Children,
                    Version = dataResult.Stat.getVersion()
                });
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not get resources as the resources node does not exist: " + e);
                return new ZkResponse<ResourcesZnode>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not get resources as the connection has been lost: " + e);
                return new ZkResponse<ResourcesZnode>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not get resources as the session has expired: " + e);
                return new ZkResponse<ResourcesZnode>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not get resources: " + e);
                return new ZkResponse<ResourcesZnode>(ZkResult.UnexpectedError);
            }
        }
        
        public async Task<ZkResponse<int>> SetResourcesAsync(ResourcesZnode resourcesZnode)
        {
            try
            {
                var data = System.Text.Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(resourcesZnode.ResourceAssignments));
                var stat = await zookeeper.setDataAsync(this.resourcesPath, data, resourcesZnode.Version);
                return new ZkResponse<int>(ZkResult.Ok, stat.getVersion());
            }
            catch (KeeperException.BadVersionException e)
            {
                this.logger.Error(this.clientId, "Could not set resource assignments due to a bad version number. " + e);
                return new ZkResponse<int>(ZkResult.BadVersion);
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set resource assignments as the node does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set resource assignments as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set resource assignments as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set resource assignments: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResult> RemoveResourceBarrierAsync(string resource)
        {
            try
            {
                await this.zookeeper.deleteAsync($"{this.resourcesPath}/{resource}/barrier");
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException)
            {
                return ZkResult.Ok;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not remove barrier from the resource as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not remove barrier from the resource as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not remove barrier from the resource: " + e);
                return ZkResult.UnexpectedError;
            }
        }
        
        public async Task<ZkResult> TryPutResourceBarrierAsync(string resource)
        {
            try
            {
                await this.zookeeper.createAsync(
                    $"{this.resourcesPath}/{resource}/barrier",
                    System.Text.Encoding.UTF8.GetBytes("0"),
                    ZooDefs.Ids.OPEN_ACL_UNSAFE,
                    CreateMode.EPHEMERAL);
                return ZkResult.Ok;
            }
            catch (KeeperException.NodeExistsException)
            {
                return ZkResult.NodeAlreadyExists;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not add a barrier to the resource as the resources node does not exist: " +
                                  e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not add a barrier to the resource as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not add a barrier to the resource as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not add a barrier to the resource: " + e);
                return ZkResult.UnexpectedError;
            }
        }
        
        public async Task<ZkResponse<List<string>>> GetStoppedAsync()
        {
            try
            {
                var childrenResult = await this.zookeeper.getChildrenAsync(this.stoppedPath);
                return new ZkResponse<List<string>>(ZkResult.Ok, childrenResult.Children);
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not get stopped clients as the stopped node does not exist: " + e);
                return new ZkResponse<List<string>>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not get stopped clients as the connection has been lost: " + e);
                return new ZkResponse<List<string>>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not get stopped clients as the session has expired: " + e);
                return new ZkResponse<List<string>>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not get stopped clients: " + e);
                return new ZkResponse<List<string>>(ZkResult.UnexpectedError);
            }
        }

        public async Task<ZkResponse<int>> WatchEpochAsync(Watcher watcher)
        {
            try
            {
                var stat = await zookeeper.existsAsync(this.epochPath, watcher);
                return new ZkResponse<int>(ZkResult.Ok, stat.getVersion());
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on epoch as the epoch znode does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on epoch as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on epoch as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on epoch: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }
        
        public async Task<ZkResponse<StatusZnode>> WatchStatusAsync(Watcher watcher)
        {
            try
            {
                var dataResult = await zookeeper.getDataAsync(this.statusPath, watcher);
                return new ZkResponse<StatusZnode>(ZkResult.Ok, new StatusZnode()
                {
                    RebalancingStatus = (RebalancingStatus) BitConverter.ToInt32(dataResult.Data, 0),
                    Version = dataResult.Stat.getVersion()
                });
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on status as the status znode does not exist: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on status as the connection has been lost: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on status as the session has expired: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on status: " + e);
                return new ZkResponse<StatusZnode>(ZkResult.UnexpectedError);
            }
        }
        
        public async Task<ZkResult> WatchResourcesChildrenAsync(Watcher watcher)
        {
            try
            {
                await this.zookeeper.getChildrenAsync(this.resourcesPath, watcher);
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on resources children as the resources znode does not exist: " + e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on resources as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on resources as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on resources: " + e);
                return ZkResult.UnexpectedError;
            }
        }

        public async Task<ZkResponse<int>> WatchResourcesDataAsync(Watcher watcher)
        {
            try
            {
                var data = await this.zookeeper.getDataAsync(this.resourcesPath, watcher);
                return new ZkResponse<int>(ZkResult.Ok, data.Stat.getVersion());
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a data watch on resource znode as the resources znode does not exist: " + e);
                return new ZkResponse<int>(ZkResult.NoZnode);
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a data watch on resource znode as the connection has been lost: " + e);
                return new ZkResponse<int>(ZkResult.ConnectionLost);
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a data watch on resource znode as the session has expired: " + e);
                return new ZkResponse<int>(ZkResult.SessionExpired);
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a data watch on resource znode: " + e);
                return new ZkResponse<int>(ZkResult.UnexpectedError);
            }
        }
        
        public async Task<ZkResult> WatchNodesAsync(Watcher watcher)
        {
            try
            {
                await this.zookeeper.getChildrenAsync(this.clientsPath, watcher);
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on clients as the clients znode does not exist: " + e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on clients as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on clients as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on clients: " + e);
                return ZkResult.UnexpectedError;
            }
        }
        
        public async Task<ZkResult> WatchSiblingNodeAsync(string siblingPath, Watcher watcher)
        {
            try
            {
                await this.zookeeper.getDataAsync(siblingPath, watcher);
                return ZkResult.Ok;
            }
            catch (KeeperException.NoNodeException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on a sibling client as the node does not exist: " + e);
                return ZkResult.NoZnode;
            }
            catch (KeeperException.ConnectionLossException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on a sibling client as the connection has been lost: " + e);
                return ZkResult.ConnectionLost;
            }
            catch (KeeperException.SessionExpiredException e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on a sibling client as the session has expired: " + e);
                return ZkResult.SessionExpired;
            }
            catch (Exception e)
            {
                this.logger.Error(this.clientId, "Could not set a watch on a sibling client: " + e);
                return ZkResult.UnexpectedError;
            }
        }
    }
}