using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.StockManagement.Models;
using Mermer.Data.Models;
using Mermer.Mvvm.Services;
using System;
using System.Collections.Generic;
using System.Linq;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Transactions;

public class StockTransactionDetailsLineEditViewModel(
  IMvxMessenger messenger,
  IMvxNavigationService navigationService,
  IUserInteractionService userInteractionService) :
  TransactionLineEditViewModel<StockTransactionDetailsLineEditViewModel.Params, StockTransactionDetailsLineEditViewModel.Result>(messenger, navigationService, userInteractionService)
{
    public override void Prepare(
      StockTransactionDetailsLineEditViewModel.Params parameter)
    {
        base.Prepare(parameter);
        parameter.PriceChanged = false;
    }

    public class Params : StockTransactionDetailsLineEditViewModel.Result
    {
        public string StockCode { get; set; }

        public string StockName { get; set; }

        public DateTime? ActionDate { get; set; }

        public IEnumerable<StockUnit> Units { get; set; }

        public IEnumerable<Currency> Currencies { get; set; }

        protected override void CalculatePriceOnUnitChange(string prevUnitId)
        {
            if (prevUnitId == this.UnitId)
                return;

            StockUnit stockUnit1 = this.Units?.SingleOrDefault(x => x.Id == prevUnitId);
            StockUnit stockUnit2 = this.Units?.SingleOrDefault(x => x.Id == this.UnitId);

            if (stockUnit1 != null && stockUnit2 != null && stockUnit1.Multiplier != 0 && stockUnit2.Divider != 0)
            {
                this.SetPriceDirectly(this.Price * stockUnit1.Divider / stockUnit1.Multiplier * stockUnit2.Multiplier / stockUnit2.Divider);
            }
            this.PriceChanged = false;
        }

        protected override void CalculatePriceOnCurrencyChange(string prevCurrencyId)
        {
            if (prevCurrencyId == this.CurrencyId || this.Currencies == null)
                return;

            Currency prevCurrency = this.Currencies.SingleOrDefault(x => x.Id == prevCurrencyId);
            Currency newCurrency = this.Currencies.SingleOrDefault(x => x.Id == this.CurrencyId);

            if (prevCurrency == null || newCurrency == null)
                return;

            DateTime checkDate = this.ActionDate ?? DateTime.Today;

            CurrencyRate prevRate = GetActiveRate(prevCurrency, checkDate);
            CurrencyRate newRate = GetActiveRate(newCurrency, checkDate);

            // Проверяем делители на ноль
            if (prevRate != null && newRate != null && prevRate.Divider != 0 && newRate.Multiplier != 0)
            {
                // 1. Переводим цену исходной валюты в базовую
                decimal basePrice = this.Price * prevRate.Multiplier / prevRate.Divider;

                // 2. Переводим из базовой в новую валюту (делим на курс новой валюты)
                decimal convertedPrice = basePrice * newRate.Divider / newRate.Multiplier;

                // Округляем до разрядности целевой валюты
                decimal roundedPrice = decimal.Round(convertedPrice, newCurrency.Decimals, MidpointRounding.AwayFromZero);

                this.SetPriceDirectly(roundedPrice);
            }

            this.PriceChanged = false;
        }

        private CurrencyRate GetActiveRate(Currency currency, DateTime targetDate)
        {
            if (currency.IsDefault)
                return new CurrencyRate { Multiplier = 1m, Divider = 1m };

            if (currency.Rates == null || !currency.Rates.Any())
                return new CurrencyRate { Multiplier = 1m, Divider = 1m };

            return currency.Rates
              .Where(r => r.ValidFrom <= targetDate)
              .OrderByDescending(r => r.ValidFrom)
              .FirstOrDefault()
              ?? currency.Rates.OrderBy(r => r.ValidFrom).FirstOrDefault()
              ?? new CurrencyRate { Multiplier = 1m, Divider = 1m };
        }
    }

    public class Result : BindableObject
    {
        public bool PriceChanged;
        private string _unitId;
        private decimal _price;
        private string _currencyId;

        public decimal Quantity { get; set; }

        public string UnitId
        {
            get => this._unitId;
            set
            {
                string unitId = this._unitId;
                this.SetProperty(ref this._unitId, value, nameof(UnitId));
                if (string.IsNullOrEmpty(unitId))
                    return;
                this.CalculatePriceOnUnitChange(unitId);
            }
        }

        public decimal Price
        {
            get => this._price;
            set
            {
                this.SetProperty(ref this._price, value, nameof(Price));
                this.PriceChanged = true;
            }
        }

        public void SetPriceDirectly(decimal value)
        {
            this.SetProperty(ref this._price, value, nameof(Price));
        }

        public string CurrencyId
        {
            get => this._currencyId;
            set
            {
                string currencyId = this._currencyId;
                this.SetProperty(ref this._currencyId, value, nameof(CurrencyId));
                if (string.IsNullOrEmpty(currencyId))
                    return;
                this.CalculatePriceOnCurrencyChange(currencyId);
            }
        }

        protected virtual void CalculatePriceOnUnitChange(string prevUnitId)
        {
        }

        protected virtual void CalculatePriceOnCurrencyChange(string prevCurrencyId)
        {
        }
    }
}