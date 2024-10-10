using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Orders;
using QuantConnect.Scheduling;
using System;
using System.Text;

namespace NineNine {

    /// <summary>
    /// Algo that randomly selects a new symbol every day.
    /// Uses Stocks/ETFs in backtesting and CFD equivalents in live mode (see https://www.quantconnect.com/docs/v2/writing-algorithms/securities/asset-classes/cfd/requesting-data),.
    /// </summary>
    public class RandomDailyAlgo : QCAlgorithm {
        // See https://www.interactivebrokers.ie/en/trading/products-exchanges.php for list of supported CFD products
        private readonly string[] _tickers = new[] { "AIG", "BAC", "IBM", "SPY" };
        private SecurityType _securityType = SecurityType.Equity;
        private string _market = Market.USA;

        private const string ENTRY_PRICE = nameof(ENTRY_PRICE);
        private const Resolution RESOLUTION = Resolution.Minute;

        // selected symbol
        private Symbol _symbol;

        // scheduling event to open position after market open
        private ScheduledEvent openEvent;

        /// <summary>
        /// Initialize algorithm.
        /// </summary>
        public override void Initialize() {
            SetTimeZone("Europe/Stockholm");
            SetStartDate(2013, 10, 04);
            SetEndDate(2013, 10, 11);

            Debug($"Algorithm Mode: {AlgorithmMode}. Deployment Target: {DeploymentTarget}. TimeZone: {TimeZone.Id}");
            
            if (LiveMode) {
                _securityType = SecurityType.Cfd;
                _market = Market.InteractiveBrokers;
            }

            // select random symbol to start the algo
             SelectSymbol();
        }
        
        /// <summary>
        /// Manage position
        /// </summary>
        /// <param name="slice"></param>
        public override void OnData(Slice slice) {
            foreach (var kv in slice) {
                var symbol = kv.Key;
                if (Portfolio[symbol].Invested) {
                    var data = kv.Value;
                    var security = Securities[symbol];
                    security.TryGet<decimal>(ENTRY_PRICE, out var entryPrice);
                    Debug($"{Time}: Managing position for {symbol.Value}. Entry: {entryPrice}. Current: {data.Price}");

                    // TODO: manage position
                } else {
                    Debug($"{Time}: Skipping {symbol.Value}. Not invested.");
                }
            }
        }

        /// <summary>
        /// Close position on end of day.
        /// </summary>
        /// <param name="symbol"></param>
        public override void OnEndOfDay(Symbol symbol) {
            var security = Securities[_symbol];
            Debug($"{Time}: Closing position for {_symbol} at {security.LocalTime} ({security.Exchange.TimeZone})");

            RemoveSecurity(symbol);

            // select a new symbol
            SelectSymbol();
        }

        /// <summary>
        /// Log order events.
        /// </summary>
        /// <param name="orderEvent"></param>
        public override void OnOrderEvent(OrderEvent orderEvent) {
            Debug($"{Time}: {orderEvent}");
        }

        /// <summary>
        /// Open position in selected symbol.
        /// </summary>
        private void OpenPosition() {
            var security = Securities[_symbol];

            Debug($"{Time}: Opening position for {_symbol} at {security.LocalTime} ({security.Exchange.TimeZone}). Entry: {security.Price}");

            // keep track of entry price
            security[ENTRY_PRICE] = security.Price;

            // TODO: calculate optimal quantity
            var quantity = security.SymbolProperties.LotSize * 2;
                
            // Place market order
            // REVIEW: use trailing stop loss?
            var ticket = MarketOrder(_symbol, quantity);
            Debug($"{Time}: {ticket}");
        }

        /// <summary>
        /// Select random symbol.
        /// </summary>
        private void SelectSymbol() {
            // Select random symbol
            // NOTE: should check for stationarity, but for now we assume all symbols are non stationary
            var i = Random.Shared.Next(_tickers.Length);
            _symbol = QuantConnect.Symbol.Create(_tickers[i], _securityType, _market);
            Debug($"{Time}: Selected {_symbol.Value}");

            // Add security to the algorithm
            var security = AddSecurity(_symbol, RESOLUTION);

            // Log market hours
            var hours = security.Exchange.Hours;
            var sb = new StringBuilder();
            sb.AppendLine($"Market hours for {_symbol.Value} ({hours.TimeZone}):");
            foreach (var day in hours.MarketHours.Values) {
                if (day.IsOpenAllDay || day.IsClosedAllDay) {
                    sb.AppendLine($"  {day.DayOfWeek}: {day}");
                } else {
                    sb.AppendLine($"  {day}");
                }
            }
            Debug(sb.ToString());

            // Remove existing event (if any)
            if (openEvent != null) {
                Schedule.Remove(openEvent);
            }

            // Schedule (new) event to open position after market opens
            openEvent = Schedule.On(DateRules.EveryDay(), TimeRules.AfterMarketOpen(_symbol, 10), OpenPosition);
        }
    }
}
