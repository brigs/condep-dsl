using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using ConDep.Dsl.Operations.LoadBalancer;

namespace ConDep.Dsl.Sequence
{
    public class RemoteSequences : IExecute
    {
        private readonly IEnumerable<ServerConfig> _servers;
        private readonly LoadBalancerExecutorBase _loadBalancer;
        private readonly bool _paralell;
        internal readonly List<RemoteSequence> _sequences = new List<RemoteSequence>();

        public RemoteSequences(IEnumerable<ServerConfig> servers, LoadBalancerExecutorBase loadBalancer, bool paralell)
        {
            _servers = servers;
            _loadBalancer = loadBalancer;
            _paralell = paralell;
            foreach (var server in servers)
            {
                var remoteSequence = new RemoteSequence(server, paralell);
                _sequences.Add(remoteSequence);
            }
        }

        public void Add(RemoteSequence sequence)
        {
            _sequences.Add(sequence);
        }

        public void ExecuteFirst(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            if (_sequences.Count != 0)
            {
                RemoteSequence sequence;
                if (settings.Options.ContinueAfterMarkedServer && _loadBalancer is StickyLoadBalancerExecutor)
                {
                    sequence = _sequences.SingleOrDefault(x => x.Server.StopServer) ?? _sequences[0];
                }
                else
                {
                    sequence = _sequences[0];
                }
                ExecuteSequence(sequence, status, settings, token);
            }
        }

        public void ExecuteRemaining(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            if (_sequences.Count > 1)
            {
                if (settings.Options.ContinueAfterMarkedServer && _loadBalancer is StickyLoadBalancerExecutor)
                {
                    var sequenceNotToExecute = _sequences.SingleOrDefault(x => x.Server.StopServer) ?? _sequences[0];
                    foreach (var sequence in _sequences.Where(sequence => sequence != sequenceNotToExecute))
                    {
                        ExecuteSequence(sequence, status, settings, token);
                    }
                }
                else
                {
                    _sequences.Skip(1).ToList().ForEach(x => ExecuteSequence(x, status, settings, token));
                }
            }
        }

        public void ExecuteSequence(RemoteSequence sequence, IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            var errorDuringLoadBalancing = false;

            try
            {
                _loadBalancer.BringOffline(sequence.Server, status, settings, token);
                sequence.Execute(status, settings, token);
            }
            catch
            {
                errorDuringLoadBalancing = true;
                throw;
            }
            finally
            {
                if (!errorDuringLoadBalancing && !settings.Options.StopAfterMarkedServer)
                {
                    _loadBalancer.BringOnline(sequence.Server, status, settings, token);
                }
            }
        }

        public void Execute(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            var errorDuringLoadBalancing = false;

            foreach (var sequence in _sequences)
            {
                try
                {
                    Logger.Info(string.Format("Taking server [{0}] offline in load balancer.", sequence.Server.Name));
                    _loadBalancer.BringOffline(sequence.Server, status, settings, token);
                    sequence.Execute(status, settings, token);
                }
                catch
                {
                    errorDuringLoadBalancing = true;
                    throw;
                }
                finally
                {
                    if (!errorDuringLoadBalancing)
                    {
                        Logger.Info(string.Format("Taking server [{0}] online in load balancer.", sequence.Server.Name));
                        _loadBalancer.BringOnline(sequence.Server, status, settings, token);
                    }
                }
            }
        }

        public string Name { get; private set; }
        public void DryRun()
        {
            foreach (var sequence in _sequences)
            {
                sequence.DryRun();
            }
        }

        public void DryRunFirst()
        {
            if (_sequences.Count != 0)
            {
                var sequence = _sequences[0];
                sequence.DryRun();
            }
        }

        public void DryRunRemaining()
        {
            if (_sequences.Count > 1)
            {
                _sequences.Skip(1).ToList().ForEach(x => x.DryRun());
            }
        }

        public List<RemoteSequence> Sequenceses {get { return _sequences; }}
    }
}