using System;
using System.Threading.Tasks;
using Mermer.Common.Settings;
using Mermer.Services;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockCodeGenerator : IStockCodeGenerationService
    {
        private readonly IConfigurator _configurator;
        private static readonly object _syncLock = new object();

        public ApiStockCodeGenerator(IConfigurator configurator)
        {
            _configurator = configurator;
        }

        public Task<string> GetNextCode()
        {
            lock (_syncLock)
            {
                AppSettings config = _configurator.GetConfig<AppSettings>() ?? new AppSettings();

                int codeValue = config.LastStockCodeValue;
                codeValue++;

                // EAN-8: 2 цифры префикса + 5 цифр порядкового номера
                string baseCode = $"{config.LocalCodePrefix:D2}{codeValue:D5}";
                string checksum = EanChecksumHelper.CalculateChecksumDigit(baseCode);
                string fullCode = baseCode + checksum;

                config.LastStockCodeValue = codeValue;
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