using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public class DbAdapter
    {
        private readonly string sqlConnectionString;

        public DbAdapter(AppSecrets appSecrets)
        {
            sqlConnectionString = appSecrets.SqlConnectionString;
        }

        private IDbConnection CreateDbConnection() => 
            new SqlConnection(sqlConnectionString);

        public async Task<int> InsertNewOperationAsync(string holdAsset, string tradeAsset, decimal buyOrderQuoteQty, string processId)
        {
            var query = @"insert into Tradings (Stage, CreatedAt, HoldAsset, TradeAsset, BuyOrderQuoteQty, UpdatedAt, ProcessId)
                    output inserted.Id
                    values(@Stage, @CreatedAt, @HoldAsset, @TradeAsset, @BuyOrderQuoteQty, @UpdatedAt, @ProcessId)";

            using(var db = CreateDbConnection())
            {
                
                var parameters = new 
                { 
                    Stage = Stage.JustRegistered, 
                    CreatedAt = DateTimeOffset.Now, 
                    HoldAsset = holdAsset, 
                    TradeAsset = tradeAsset,
                    BuyOrderQuoteQty = buyOrderQuoteQty,
                    UpdatedAt = DateTimeOffset.Now,
                    ProcessId = processId
                };

                try
                {
                    return await db.QuerySingleAsync<int>(query, parameters);
                }
                catch(SqlException e) when (Regex.IsMatch(e.Message, ".*IdxActive1.*duplicate key.*", RegexOptions.IgnoreCase))
                {
                    throw new TradingException(TradingError.ThereIsAnotherTradingInExecution);
                }
            }
        }

        public async Task<Trading> GetActiveTradingAsync(string holdAsset, Stage? stage = null, string processId = null)
        {
            var query = "select * from Tradings where Active = 1 and HoldAsset = @HoldAsset";

            if(stage != null)
            {
                query += " and Stage = @Stage";
            }

            if(processId != null)
            {
                query += " and ProcessId = @ProcessId";
            }

            using (var db = CreateDbConnection())
            {
                return await db.QuerySingleOrDefaultAsync<Trading>(query, new { HoldAsset = holdAsset, Stage = stage, ProcessId = processId });
            }
        }

        public async Task UpdateTradingProcessIdAsync(int id, string newProcessId)
        {
            using (var db = CreateDbConnection())
            {
                await db.ExecuteAsync("update Tradings set ProcessId = @ProcessId where Id = @Id", new { Id = id, ProcessId = newProcessId });
            }
        }

        public async Task<Trading> GetTradingAsync(int id)
        {
            var query = "select * from Tradings where Id = @Id";

            using (var db = CreateDbConnection())
            {
                return await db.QuerySingleOrDefaultAsync<Trading>(query, new { Id = id });
            }
        }

        public async Task<decimal> GetStopThresholdAsync(string holdAsset)
        {
            using(var db = CreateDbConnection())
            {
                return await db.QuerySingleAsync<decimal>("select StopThreshold from StopLossControl where HoldAsset = @HoldAsset", new { HoldAsset = holdAsset });
            }
        }

        public async Task IncreaseStopThresholdAsync(string holdAsset, decimal percentage)
        {
            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync("update StopLossControl set StopThreshold = StopThreshold * @StopThresholdIncrement, UpdatedAt = @UpdatedAt where HoldAsset = @HoldAsset",
                    new
                    {
                        HoldAsset = holdAsset,
                        StopThresholdIncrement = percentage / 100m + 1m,
                        UpdatedAt = DateTimeOffset.Now
                    }));
            }
        }

        public async Task<bool> AnyActiveTradeAsync(string holdAsset)
        {
            using (var db = CreateDbConnection())
            {
                return await db.
                    QuerySingleOrDefaultAsync<bool?>("select Active from Tradings where HoldAsset = @HoldAsset and Active = 1", new { HoldAsset = holdAsset }) ??
                    false;
            }
        }

        public async Task UpdateTradeCompletedAndNotInitializedStageAsync(int id, string abortReason, string processId)
        {
            using(var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(@"update Tradings set Stage = @Stage, Active = 0, UpdatedAt = @UpdatedAt, CompletedAt = @CompletedAt, AbortReason = @AbortReason where Id = @Id and ProcessId = @ProcessId", 
                    new { Id = id, Stage = Stage.CompletedAndNotInitialized, UpdatedAt = DateTimeOffset.Now, CompletedAt = DateTimeOffset.Now, AbortReason = abortReason, ProcessId = processId }));
            }
        }

        public async Task UpdateOrderCreatedStageAsync(int id, string processId)
        {
            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync("update Tradings set Stage = @Stage, BuyOrderCreatedAt = @BuyOrderCreatedAt, UpdatedAt = @UpdatedAt where Id = @Id and ProcessId = @ProcessId", 
                    new { Id = id, Stage = Stage.BuyOrderCreated, BuyOrderCreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now, ProcessId = processId }));
            }
        }

        public async Task UpdateTradingCreatingBuyOrderStageAsync(int id, string processId)
        {
            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync("update Tradings set Stage = @Stage, UpdatedAt = @UpdatedAt where Id = @Id and ProcessId = @ProcessId", 
                    new { Id = id, Stage = Stage.CreatingBuyOrder, UpdatedAt = DateTimeOffset.Now, ProcessId = processId }));

            }
        }

        public async Task UpdateBuyOrderFilledStageAsync(Order order, string processId)
        {
            var query = @"update Tradings set 
                                Stage = @Stage,
                                BuyOrderQuoteQty = @BuyOrderQuoteQty,
                                BuyOrderCreatedAt = @BuyOrderCreatedAt,
                                BuyOrderFilledAt = @BuyOrderFilledAt,
                                BuyPrice = @BuyPrice,
                                TradeAssetQty = @TradeAssetQty,
                                UpdatedAt = @UpdatedAt
                              where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = order.TradingId,
                Stage = Stage.BuyOrderFilled,
                BuyOrderQuoteQty = order.CummulativeQuoteQty,
                BuyOrderCreatedAt = order.CreatedAt,
                BuyOrderFilledAt = order.UpdatedAt,
                BuyPrice = order.Price,
                TradeAssetQty = order.ExecutedQty,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateSellOrderFilledStageAsync(Order order, string processId)
        {
            var query = @"update Tradings set 
                                Stage = @Stage,
                                SellOrderFilledAt = @SellOrderFilledAt,
                                SellOrderExecutedPrice = @SellOrderExecutedPrice,
                                TradeAssetQty = @TradeAssetQty,
                                SellOrderKind = @SellOrderKind,
                                CompletedAt = @CompletedAt,
                                UpdatedAt = @UpdatedAt,
                                Active = 0
                              where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = order.TradingId,
                Stage = Stage.SellOrderFilled,
                SellOrderFilledAt = order.UpdatedAt,
                SellOrderExecutedPrice = order.Price,
                TradeAssetQty = order.ExecutedQty,
                SellOrderKind = order.OrderKind,
                CompletedAt = order.UpdatedAt,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateSellOrderReadTimeAsync(int id, string processId)
        {
            using (var db = CreateDbConnection())
            {
                await db.ExecuteAsync("update Tradings set SellOrderLastReadAt = @SellOrderLastReadAt, UpdatedAt = @UpdatedAt where Id = @id and ProcessId = @ProcessId", 
                    new { Id = id, SellOrderLastReadAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now, ProcessId = processId });
            }
        }

        public async Task UpdateSellOrderParametersCalculatedStageAsync(int id, decimal sellPrice, decimal sellStopLimitPrice, decimal rollbackPrice, string processId)
        {
            var query = @"update Tradings set 
                                Stage = @Stage,
                                SellPrice = @SellPrice,
                                SellStopLimitPrice = @SellStopLimitPrice,
                                RollbackPrice = @RollbackPrice,
                                UpdatedAt = @UpdatedAt
                              where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.ParametersCalculated,
                SellPrice = sellPrice,
                SellStopLimitPrice = sellStopLimitPrice,
                RollbackPrice = rollbackPrice,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateSellOrderCreatedStageAsync(int id, string processId)
        {
            var query = @"update Tradings set 
                                Stage = @Stage,
                                SellOrderCreatedAt = @SellOrderCreatedAt,
                                UpdatedAt = @UpdatedAt
                              where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.SellOrderCreated,
                SellOrderCreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateMaxPriceAsync(int id, decimal maxPrice, string processId)
        {
            var query = @"update Tradings set 
                                MaxPriceRead = @MaxPriceRead,
                                MaxPriceReadAt = @MaxPriceReadAt,
                                UpdatedAt = @UpdatedAt
                            where id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                MaxPriceRead = maxPrice,
                MaxPriceReadAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using(var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateSellOrderCreatingStageAsync(int id, string processId)
        {
            var query = @"update Tradings set 
                                Stage = @Stage,
                                SellOrderCreatedAt = @SellOrderCreatedAt,
                                UpdatedAt = @UpdatedAt
                              where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.CreatingSellOrder,
                SellOrderCreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateMinPriceAsync(int id, decimal minPrice, string processId)
        {
            var query = @"update Tradings set 
                                MinPriceRead = @MinPriceRead,
                                MinPriceReadAt = @MinPriceReadAt,
                                UpdatedAt = @UpdatedAt
                            where id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                MinPriceRead = minPrice,
                MinPriceReadAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        // For test purpose only
        public async Task InactivateAllAsync(string holdAsset)
        {
            var query = @"update Tradings set
                                Active = 0,
                                UpdatedAt = @UpdatedAt
                            where HoldAsset = @HoldAsset";

            var parameters = new
            {
                HoldAsset = holdAsset,
                UpdatedAt = DateTimeOffset.Now
            };

            using(var db = CreateDbConnection())
            {
                await db.ExecuteAsync(query, parameters);
            }
        }

        private void EnsureAffectedExactlyOneRow(int affectedRows)
        {
            if (affectedRows != 1)
            {
                throw new InvalidOperationException($"Exceptected 1 affected row but {affectedRows}");
            }
        }

        public async Task UpdateRollbackStageCancellingOcoOrderAsync(int id, string processId)
        {
            var query = @"update Tradings set
                                Stage = @Stage,
                                IsRollback = 1,
                                UpdatedAt = @UpdatedAt
                            where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.RollbackCancellingOcoOrder,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateRollbackStageCancelOcoOrderExecutedAsync(int id, string processId)
        {
            var query = @"update Tradings set
                                Stage = @Stage,
                                UpdatedAt = @UpdatedAt
                            where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.RollbackCancelOcoOrderExecuted,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }

        public async Task UpdateRollbackStageOcoOrderCancelledAsync(int id, string processId)
        {
            var query = @"update Tradings set
                                Stage = @Stage,
                                UpdatedAt = @UpdatedAt
                            where Id = @Id and ProcessId = @ProcessId";

            var parameters = new
            {
                Id = id,
                Stage = Stage.RollbackCancelOcoOrderCancelled,
                UpdatedAt = DateTimeOffset.Now,
                ProcessId = processId
            };

            using (var db = CreateDbConnection())
            {
                EnsureAffectedExactlyOneRow(await db.ExecuteAsync(query, parameters));
            }
        }
    }
}
