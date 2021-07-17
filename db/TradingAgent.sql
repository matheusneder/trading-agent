IF OBJECT_ID (N'dbo.Tradings', N'U') IS NOT NULL  
	DROP TABLE dbo.Tradings
GO
CREATE TABLE [dbo].[Tradings]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
	[Stage] INT NOT NULL, 
	[CreatedAt] DATETIMEOFFSET NOT NULL,
	HoldAsset NVARCHAR(10) NOT NULL, 
	TradeAsset  NVARCHAR(10) NOT NULL, 
    [BuyOrderQuoteQty] DECIMAL(18, 9) NOT NULL,
    [BuyOrderCreatedAt] DATETIMEOFFSET NULL,
	[BuyOrderFilledAt] DATETIMEOFFSET NULL,
	[BuyPrice] DECIMAL(18, 9) NULL,
	[BuyOrderFee] DECIMAL(18, 9) NULL,
	[TradeAssetQty] DECIMAL(18, 9) NULL,
	[SellPrice] DECIMAL(18, 9) NULL,
	[SellStopLimitPrice] DECIMAL(18, 9) NULL,
	[RollbackPrice] DECIMAL(18, 9) NULL,
	[SellOrderCreatedAt] DATETIMEOFFSET NULL,
	[SellOrderFee] DECIMAL(18, 9) NULL,
	[SellOrderLastReadAt] DATETIMEOFFSET NULL,
	[MinPriceRead] DECIMAL(18, 9) NULL,
	[MinPriceReadAt] DATETIMEOFFSET NULL,
	[MaxPriceRead] DECIMAL(18, 9) NULL,
	[MaxPriceReadAt] DATETIMEOFFSET NULL,
	[SellOrderFilledAt] DATETIMEOFFSET NULL,	
	[SellOrderExecutedPrice] DECIMAL(18, 9) NULL,
	[SellOrderKind] INT NULL,
	[IsRollback] BIT NOT NULL DEFAULT 0,
	[CompletedAt] DATETIMEOFFSET NULL,
	[UpdatedAt] DATETIMEOFFSET NULL,
	[AbortReason] NVARCHAR(MAX) NULL,
	[ProcessId] NVARCHAR(50) NOT NULL,
	[Active] BIT NOT NULL DEFAULT 1
);
GO
CREATE UNIQUE NONCLUSTERED INDEX IdxActive1 ON [dbo].[Tradings](HoldAsset, [Active]) WHERE [Active] = 1;

GO
IF OBJECT_ID (N'dbo.[StopLossControl]', N'U') IS NOT NULL  
	DROP TABLE [StopLossControl]
GO
CREATE TABLE [dbo].[StopLossControl]
(
	HoldAsset NVARCHAR(8) PRIMARY KEY,
	[InitialAmount] DECIMAL(18, 9) NOT NULL,
	[CreatedAt] DATETIMEOFFSET NOT NULL,
	[StopThreshold] DECIMAL(18, 9) NOT NULL,
	[UpdatedAt] DATETIMEOFFSET NOT NULL
)
GO
INSERT INTO [StopLossControl] (HoldAsset, [InitialAmount], [CreatedAt], [StopThreshold], [UpdatedAt]) 
	VALUES (N'BUSD', 200, sysdatetimeoffset(), 190, sysdatetimeoffset())

go
IF OBJECT_ID (N'dbo.[Vw_Tradings]', N'U') IS NOT NULL  
	DROP VIEW [Vw_Tradings]
GO
create view Vw_Tradings as
select
		Id,
		Stage,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, CreatedAt, 1)), '-03:00') CreatedAt,
		HoldAsset,
		TradeAsset,
		BuyOrderQuoteQty,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, BuyOrderCreatedAt, 1)), '-03:00') BuyOrderCreatedAt,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, BuyOrderFilledAt, 1)), '-03:00') BuyOrderFilledAt,
		BuyPrice,
		BuyOrderFee,
		TradeAssetQty,
		SellPrice,
		SellStopLimitPrice,
		RollbackPrice,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, SellOrderCreatedAt, 1)), '-03:00') SellOrderCreatedAt,
		SellOrderFee,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, SellOrderLastReadAt, 1)), '-03:00') SellOrderLastReadAt,
		MinPriceRead,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, MinPriceReadAt, 1)), '-03:00') MinPriceReadAt,
		MaxPriceRead,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, MaxPriceReadAt, 1)), '-03:00') MaxPriceReadAt,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, SellOrderFilledAt, 1)), '-03:00') SellOrderFilledAt,
		SellOrderExecutedPrice,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, CompletedAt, 1)), '-03:00') CompletedAt,
		todatetimeoffset(dateadd(hour, -3, convert(datetime2, UpdatedAt, 1)), '-03:00') UpdatedAt,
		SellOrderKind,
		IsRollback,
		ProcessId,
		Active
	from Tradings

go
IF OBJECT_ID (N'dbo.[SingleTradeAsForm]', N'U') IS NOT NULL  
	DROP PROCEDURE [SingleTradeAsForm]
GO
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

