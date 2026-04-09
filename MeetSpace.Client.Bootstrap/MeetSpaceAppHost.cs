using MeetSpace.Client.Media.Abstractions;
using MeetSpace.Client.Realtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Bootstrap
{
    public sealed class MeetSpaceAppHost : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _started;
        private bool _disposed;

        internal MeetSpaceAppHost(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IServiceProvider Services
        {
            get { return _serviceProvider; }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_started)
                return;

            var warmup = _serviceProvider.GetRequiredService<BootstrapWarmupService>();
            await warmup.WarmupAsync(cancellationToken).ConfigureAwait(false);

            var mediaEngine = _serviceProvider.GetService<IMediaEngine>();
            if (mediaEngine != null)
                await mediaEngine.InitializeAsync(cancellationToken).ConfigureAwait(false);

            _started = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || !_started)
                return;

            try
            {
                var gateway = _serviceProvider.GetService<IRealtimeGateway>();
                if (gateway != null && gateway.IsConnected)
                    await gateway.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                var mediaEngine = _serviceProvider.GetService<IMediaEngine>();
                if (mediaEngine != null)
                    await mediaEngine.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            _started = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MeetSpaceAppHost));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _serviceProvider.Dispose();
        }
    }
}