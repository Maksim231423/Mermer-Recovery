using Couchbase.Core;
using Couchbase.Views;
using Mermer.Common.Services;
using Mermer.Core.Couch.Common;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Core.Couch;

public class CouchDocumentChangeListener : IDocumentChangeListener
{
    private readonly ICouchCluster _cluster;
    private readonly IDocumentChangedNotifier _notifier;
    private CancellationTokenSource _cancellationTokenSource;

    public CouchDocumentChangeListener(ICouchCluster cluster, IDocumentChangedNotifier notifier)
    {
        _cluster = cluster;
        _notifier = notifier;
    }

    public bool Started { get; private set; }
    public int UpdateInterval { get; private set; }
    public string LastRevision { get; private set; }

    public void Start()
    {
        if (Started) return;
        Started = true;
        _cancellationTokenSource = new CancellationTokenSource();

        // Запускаем в отдельном Task, а не async void, чтобы не блокировать UI и контекст
        Task.Run(async () =>
        {
            var token = _cancellationTokenSource.Token;
            while (Started && !token.IsCancellationRequested)
            {
                try
                {
                    // Если кластер Couchbase не инициализирован или мы на PostgreSQL,
                    // просто ждём и не спамим исключениями
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Пауза при ошибке, чтобы не грузить процессор
                    try { await Task.Delay(5000, token); } catch { break; }
                }
            }
        });
    }

    public void Touch()
    {
        // Принудительный триггер при необходимости
    }

    public void Stop()
    {
        Started = false;
        try
        {
            // Обязательно отменяем токен, чтобы мгновенно убить фоновый поток
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        catch { }
    }
}