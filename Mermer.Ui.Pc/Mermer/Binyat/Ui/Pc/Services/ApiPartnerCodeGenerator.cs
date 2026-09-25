using System;
using System.Threading.Tasks;
using Mermer.Common.Settings;
using Mermer.CRM.Services;
using Mermer.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiPartnerCodeGenerator : IPartnerCodeGenerationService
    {
        private readonly IConfigurator _configurator;
        private static readonly object _syncLock = new object();

        public ApiPartnerCodeGenerator(IConfigurator configurator)
        {
            _configurator = configurator;
        }

        public Task<string> GetNextCode()
        {
            lock (_syncLock)
            {
                AppSettings config = _configurator.GetConfig<AppSettings>() ?? new AppSettings();

                int codeValue = config.LastPartnerCodeValue;
                codeValue++;

                // EAN-8: 2 цифры префикса + 5 цифр порядкового номера
                string baseCode = $"{config.LocalCodePrefix:D2}{codeValue:D5}";
                string checksum = EanChecksumHelper.CalculateChecksumDigit(baseCode);
                string fullCode = baseCode + checksum;

                config.LastPartnerCodeValue = codeValue;
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