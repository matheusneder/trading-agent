CREATE TABLE [dbo].[StopLossControl]
(
	HoldAsset NVARCHAR(8) PRIMARY KEY,
	[InitialAmount] DECIMAL(18, 9) NOT NULL,
	[CreatedAt] DATETIMEOFFSET NOT NULL,
	[StopThreshold] DECIMAL(18, 9) NOT NULL,
	[TradeMinimumAmountModeActive] BIT NOT NULL DEFAULT 0,
	[UpdatedAt] DATETIMEOFFSET NOT NULL
)