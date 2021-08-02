create procedure SingleTradeAsForm @TradingId int as
select 
    'Id' Field, (select convert(nvarchar(max), Id) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'Stage' Field, (select convert(nvarchar(max), Stage) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'CreatedAt' Field, (select convert(nvarchar(max), CreatedAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'HoldAsset' Field, (select convert(nvarchar(max), HoldAsset) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'TradeAsset' Field, (select convert(nvarchar(max), TradeAsset) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'BuyOrderQuoteQty' Field, (select convert(nvarchar(max), BuyOrderQuoteQty) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'BuyOrderCreatedAt' Field, (select convert(nvarchar(max), BuyOrderCreatedAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'BuyOrderFilledAt' Field, (select convert(nvarchar(max), BuyOrderFilledAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'BuyPrice' Field, (select convert(nvarchar(max), BuyPrice) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'BuyOrderFee' Field, (select convert(nvarchar(max), BuyOrderFee) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'TradeAssetQty' Field, (select convert(nvarchar(max), TradeAssetQty) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellPrice' Field, (select convert(nvarchar(max), SellPrice) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellStopLimitPrice' Field, (select convert(nvarchar(max), SellStopLimitPrice) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'RollbackPrice' Field, (select convert(nvarchar(max), RollbackPrice) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderCreatedAt' Field, (select convert(nvarchar(max), SellOrderCreatedAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderFee' Field, (select convert(nvarchar(max), SellOrderFee) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderLastReadAt' Field, (select convert(nvarchar(max), SellOrderLastReadAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'MinPriceRead' Field, (select convert(nvarchar(max), MinPriceRead) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'MinPriceReadAt' Field, (select convert(nvarchar(max), MinPriceReadAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'MaxPriceRead' Field, (select convert(nvarchar(max), MaxPriceRead) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'MaxPriceReadAt' Field, (select convert(nvarchar(max), MaxPriceReadAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderFilledAt' Field, (select convert(nvarchar(max), SellOrderFilledAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderExecutedPrice' Field, (select convert(nvarchar(max), SellOrderExecutedPrice) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'CompletedAt' Field, (select convert(nvarchar(max), CompletedAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'UpdatedAt' Field, (select convert(nvarchar(max), UpdatedAt) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'SellOrderKind' Field, (select convert(nvarchar(max), SellOrderKind) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'IsRollback' Field, (select convert(nvarchar(max), IsRollback) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'ProcessId' Field, (select convert(nvarchar(max), ProcessId) from Vw_Tradings where Id = @TradingId) Val
union
select 
    'Active' Field, (select convert(nvarchar(max), Active) from Vw_Tradings where Id = @TradingId) Val