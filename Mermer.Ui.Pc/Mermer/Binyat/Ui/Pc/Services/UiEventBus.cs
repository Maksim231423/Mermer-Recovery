using System;
using MvvmCross.Platform;
using MvvmCross.Plugins.Messenger;
using Mermer.Mvvm.Messages;

namespace Mermer.Ui.Pc.Services;

public static class UiEventBus
{
    private static IMvxMessenger _messenger;

    public static IMvxMessenger Messenger =>
        _messenger ??= Mvx.Resolve<IMvxMessenger>();

    /// <summary>
    /// Уведомляет открытые списки типа T об изменении документа по его Id.
    /// </summary>
    public static void NotifyDocumentModified<T>(string documentId, object sender = null)
    {
        if (string.IsNullOrEmpty(documentId)) return;

        try
        {
            var messenger = Messenger;
            if (messenger != null)
            {
                var message = new DocumentModified<T>(sender ?? messenger, documentId);
                messenger.Publish(message);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UiEventBus] Ошибка отправки уведомления: {ex.Message}");
        }
    }

    /// <summary>
    /// Перегрузка, принимающая сам объект сущности (извлекает Id автоматически).
    /// </summary>
    public static void NotifyDocumentModified<T>(T document, object sender = null)
    {
        if (document == null) return;

        string id = null;
        var prop = typeof(T).GetProperty("Id");
        if (prop != null)
        {
            id = prop.GetValue(document)?.ToString();
        }

        NotifyDocumentModified<T>(id ?? string.Empty, sender);
    }
}