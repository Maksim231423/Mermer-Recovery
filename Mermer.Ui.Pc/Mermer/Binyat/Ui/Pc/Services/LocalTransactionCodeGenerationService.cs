using System;
using System.Threading.Tasks;
using Mermer.Common.Settings;
using Mermer.Http;
using Mermer.Services;
using Mermer.Transactions.Services;

namespace Mermer.Ui.Pc.Services
{
    public class LocalTransactionCodeGenerationService : ITransactionCodeGenerationService
    {
        private readonly IConfigurator _configurator;
        private static readonly object _syncLock = new object();

        public LocalTransactionCodeGenerationService(IConfigurator configurator)
        {
            _configurator = configurator ?? throw new Exception("Configurator service cannot be null.");
        }

        public Task<string> GetNextCode()
        {
            lock (_syncLock)
            {
                try
                {
                    AppSettings config = _configurator.GetConfig<AppSettings>() ?? new AppSettings();

                    int codeValue = config.LastTransactionCodeValue;
                    codeValue++;

                    // EAN-13: 2 цифры префикса магазина (01..99) + 10 цифр порядкового номера
                    string baseCode = $"{config.LocalCodePrefix:D2}{codeValue:D10}";
                    string checksum = EanChecksumHelper.CalculateChecksumDigit(baseCode);
                    string fullCode = baseCode + checksum;

                    // Сохраняем последнее использованное значение в конфиг
                    config.LastTransactionCodeValue = codeValue;
                    _configurator.SetConfig<AppSettings>(config);

                    return Task.FromResult(fullCode);
                }
                catch (Exception ex)
                {
                    var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    throw new Exception(string.Format("Failed to generate transaction barcode. Details: {0}", detail), ex);
                }
            }
        }
    }
}