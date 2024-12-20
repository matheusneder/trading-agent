﻿CREATE TABLE [dbo].[Tradings]
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
	[UpgradePrice] DECIMAL(18, 9) NULL,
	[UpgradeCount] INT NOT NULL DEFAULT 0,
	[SellOrderCreatedAt] DATETIMEOFFSET NULL,
	[SellOrderBinanceIdSuffix] NCHAR(20) NULL,
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
