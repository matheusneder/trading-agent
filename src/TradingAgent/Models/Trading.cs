using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
	public class Trading
	{
		public int Id { get; set; }
		public Stage Stage { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public string HoldAsset { get; set; }
		public string TradeAsset { get; set; }
		public decimal BuyOrderQuoteQty { get; set; }
		public DateTimeOffset? BuyOrderCreatedAt { get; set; }
		public DateTimeOffset? BuyOrderFilledAt { get; set; }
		public decimal? BuyPrice { get; set; }
		public decimal? BuyOrderFee { get; set; }
		public decimal? TradeAssetQty { get; set; }
		public decimal? SellPrice { get; set; }
		public decimal? SellStopLimitPrice { get; set; }
		public decimal? UpgradePrice { get; set; }
		public int UpgradeCount { get; set; }
		public decimal? RollbackPrice { get;set; }
		public DateTimeOffset? SellOrderCreatedAt { get; set; }
		public string SellOrderBinanceIdSuffix { get; set; }
		public decimal? SellOrderFee { get; set; }
		public DateTimeOffset? SellOrderLastReadAt { get; set; }
		public decimal? MinPriceRead { get; set; }
		public DateTimeOffset? MinPriceReadAt { get; set; }
		public decimal? MaxPriceRead { get; set; }
		public DateTimeOffset? MaxPriceReadAt { get; set; }
		public DateTimeOffset? SellOrderFilledAt { get; set; }
		public decimal? SellOrderExecutedPrice { get; set; }
		public DateTimeOffset? CompletedAt { get; set; }
		public DateTimeOffset? UpdatedAt { get; set; }
		public OrderKind? SellOrderKind { get; set; }
		public bool IsRollback { get; set; }
		public string ProcessId { get; set; }
		public bool Active { get; set; }
	}
}
