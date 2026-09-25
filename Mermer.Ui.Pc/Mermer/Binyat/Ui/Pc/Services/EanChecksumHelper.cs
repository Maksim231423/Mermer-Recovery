using System;
using System.Linq;

namespace Mermer.Ui.Pc.Services
{
    public static class EanChecksumHelper
    {
        /// <summary>
        /// Вычисляет контрольную цифру по стандарту GS1 (EAN-8, EAN-13, UPC).
        /// </summary>
        public static string CalculateChecksumDigit(string codeWithoutChecksum)
        {
            if (string.IsNullOrEmpty(codeWithoutChecksum))
                return "0";

            int sum = 0;
            // Для нечетных с конца позиций вес 3, для четных - 1
            int multiplier = 3;

            for (int i = codeWithoutChecksum.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(codeWithoutChecksum[i]))
                {
                    int digit = codeWithoutChecksum[i] - '0';
                    sum += digit * multiplier;
                    multiplier = (multiplier == 3) ? 1 : 3;
                }
            }

            int remainder = sum % 10;
            int checksum = (remainder == 0) ? 0 : 10 - remainder;

            return checksum.ToString();
        }
    }
}