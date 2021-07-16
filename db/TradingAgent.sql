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