using System;
using System.Threading.Tasks;
using Mermer.Common.Settings;
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
            _configurator = configurator;
        }

        public Task<string> GetNextCode()
        {
            lock (_syncLock)
            {
                AppSettings config = _configurator.GetConfig<AppSettings>() ?? new AppSettings();

                int codeValue = config.LastTransactionCodeValue;
                codeValue++;

                // EAN-13: 2 цифры префикса + 10 цифр порядкового номера
                string baseCode = $"{config.LocalCodePrefix:D2}{codeValue:D10}";
                string checksum = EanChecksumHelper.CalculateChecksumDigit(baseCode);
                string fullCode = baseCode + checksum;

                // Сохраняем последнее использованное значение
                config.LastTransactionCodeValue = codeValue;
                try
                {
                    _configurator.SetConfig<AppSettings>(config);
                }
                catch { }

                return Task.FromResult(fullCode);
            }
        }
    }
}