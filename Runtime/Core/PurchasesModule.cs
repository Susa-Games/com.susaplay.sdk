using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class PurchasesModule
    {
        private readonly HttpClient _httpClient;
        private readonly string _gameId;
        private readonly Dictionary<string, TaskCompletionSource<PurchaseResult>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<PurchaseResult>>();

        public PurchasesModule(HttpClient httpClient, string gameId)
        {
            _httpClient = httpClient;
            _gameId = gameId;
        }

        public void Initialize()
        {
            // Unsubscribe first — a retried SDK init constructs a new module, and the old one
            // would otherwise stay subscribed to the bridge for the life of the page.
            WebGLBridge.OnMessageReceived -= HandleMessage;
            WebGLBridge.OnMessageReceived += HandleMessage;
        }

        /// <param name="sandbox">
        /// Requests the developer test lane. The server decides: the request is
        /// granted only for a SusaPlay admin, or a developer on a game they own.
        /// For every other caller the purchase is live regardless of this flag.
        /// Sandbox balances live in a separate wallet and never mix with real ones.
        /// </param>
        public Task<PurchaseResult> StartPurchase(bool sandbox = false)
        {
            return StartDirectItemPurchase(null, sandbox);
        }

        /// <param name="sandbox">See <see cref="StartPurchase"/> — a request, not a decision.</param>
        public Task<PurchaseResult> StartDirectItemPurchase(string itemId, bool sandbox = false)
        {
            return StartPurchaseInternal("direct_item", itemId, null, sandbox);
        }

        /// <param name="sandbox">See <see cref="StartPurchase"/> — a request, not a decision.</param>
        public Task<PurchaseResult> StartWalletTopupPurchase(string topupPackId, bool sandbox = false)
        {
            if (string.IsNullOrEmpty(topupPackId))
            {
                return Task.FromResult(new PurchaseResult
                {
                    Success = false,
                    Status = "invalid-request",
                    ErrorCode = "INVALID_ARGUMENT",
                    ErrorMessage = "topupPackId is required."
                });
            }

            return StartPurchaseInternal("wallet_topup", null, topupPackId, sandbox);
        }

        public async Task<StoreCatalogResult> GetStoreItems()
        {
            var response = await _httpClient.Get("/economy/store-items?gameId=" + _gameId);
            if (!response.Success)
            {
                return StoreCatalogResult.Fail(response.Error);
            }

            var envelope = JsonUtility.FromJson<StoreCatalogEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return StoreCatalogResult.Fail("Malformed store catalog response");
            }

            return new StoreCatalogResult
            {
                Success = true,
                GameId = envelope.data.gameId,
                Currencies = envelope.data.currencyList ?? Array.Empty<StoreCurrencyEntry>(),
                Items = envelope.data.items ?? Array.Empty<StoreItemEntry>(),
            };
        }

        public async Task<PlatformWalletResult> GetPlatformWallet()
        {
            var response = await _httpClient.Get("/economy/platform-wallet");
            if (!response.Success)
            {
                return PlatformWalletResult.Fail(response.Error);
            }

            var wallet = JsonUtility.FromJson<PlatformWalletSnapshot>(response.Data);
            if (wallet != null && !string.IsNullOrEmpty(wallet.walletId))
            {
                return new PlatformWalletResult
                {
                    Success = true,
                    Wallet = wallet,
                };
            }

            var envelope = JsonUtility.FromJson<PlatformWalletEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return PlatformWalletResult.Fail("Malformed platform wallet response");
            }

            return new PlatformWalletResult
            {
                Success = true,
                Wallet = envelope.data,
            };
        }

        public async Task<TopupPacksResult> GetTopupPacks()
        {
            var response = await _httpClient.Get("/economy/topup-packs");
            if (!response.Success)
            {
                return TopupPacksResult.Fail(response.Error);
            }

            var envelope = JsonUtility.FromJson<TopupPacksEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return TopupPacksResult.Fail("Malformed top-up pack response");
            }

            return new TopupPacksResult
            {
                Success = true,
                Packs = envelope.data.topupPacks ?? Array.Empty<TopupPackEntry>(),
            };
        }

        /// <param name="requestId">
        /// Optional idempotency key. Supply a value that is stable across retries
        /// of the same intended purchase (a GUID generated once per attempt) and a
        /// retried call returns the original result instead of debiting again.
        /// Omit it and a retry is treated as a second purchase.
        /// </param>
        /// <summary>Read what the player owns in this game.</summary>
        public async Task<InventoryResult> GetInventory()
        {
            var response = await _httpClient.Get($"/economy/inventory?gameId={Uri.EscapeDataString(_gameId ?? string.Empty)}");
            if (!response.Success)
            {
                return InventoryResult.Fail(response.Error);
            }

            var envelope = JsonUtility.FromJson<InventoryEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return InventoryResult.Fail("Malformed inventory response");
            }

            return new InventoryResult
            {
                Success = true,
                Items = envelope.data.itemsList ?? Array.Empty<StringIntEntry>(),
                Consumables = envelope.data.consumablesList ?? Array.Empty<StringIntEntry>(),
            };
        }

        /// <summary>
        /// Use up a consumable the player owns. Moves no currency — the coins were
        /// spent when the item was bought.
        /// </summary>
        /// <param name="requestId">Optional idempotency key; see <see cref="SpendPlatformWallet"/>.</param>
        public async Task<ConsumeResult> ConsumeItem(string itemId, int quantity = 1, string requestId = null)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return ConsumeResult.Fail("itemId is required.");
            }

            var body = JsonUtility.ToJson(new ConsumeRequest
            {
                gameId = _gameId,
                itemId = itemId,
                quantity = quantity < 1 ? 1 : quantity,
                requestId = requestId,
            });
            var response = await _httpClient.Post("/economy/consume", body);
            if (!response.Success)
            {
                return ConsumeResult.Fail(response.Error);
            }

            var envelope = JsonUtility.FromJson<ConsumeEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return ConsumeResult.Fail("Malformed consume response");
            }

            return new ConsumeResult
            {
                Success = true,
                ItemId = envelope.data.itemId,
                Remaining = envelope.data.remaining,
                Consumables = envelope.data.consumablesList ?? Array.Empty<StringIntEntry>(),
            };
        }

        public async Task<PlatformWalletSpendResult> SpendPlatformWallet(string itemId, string requestId = null)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return PlatformWalletSpendResult.Fail("itemId is required.");
            }

            var body = JsonUtility.ToJson(new PlatformWalletSpendRequest
            {
                gameId = _gameId,
                itemId = itemId,
                requestId = requestId,
            });
            var response = await _httpClient.Post("/economy/platform-wallet/spend", body);
            if (!response.Success)
            {
                return PlatformWalletSpendResult.Fail(response.Error);
            }

            var envelope = JsonUtility.FromJson<PlatformWalletSpendEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return PlatformWalletSpendResult.Fail("Malformed wallet spend response");
            }

            return new PlatformWalletSpendResult
            {
                Success = true,
                Wallet = envelope.data.platformWallet,
                Inventory = envelope.data.inventoryList ?? Array.Empty<StringIntEntry>(),
                Consumables = envelope.data.consumablesList ?? Array.Empty<StringIntEntry>(),
            };
        }

        private async Task<PurchaseResult> StartPurchaseInternal(
            string intent,
            string itemId,
            string topupPackId,
            bool sandbox
        )
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<PurchaseResult>();
            _pendingRequests[requestId] = tcs;

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_PURCHASE",
                payload = JsonUtility.ToJson(
                    new PurchaseRequestPayload
                    {
                        requestId = requestId,
                        intent = intent,
                        gameId = intent == "direct_item" ? _gameId : null,
                        itemId = itemId,
                        topupPackId = topupPackId,
                        sandbox = sandbox,
                    }
                )
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(180000));
            if (completed != tcs.Task)
            {
                _pendingRequests.Remove(requestId);
                return new PurchaseResult
                {
                    Success = false,
                    RequestId = requestId,
                    Status = "timeout",
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = "Purchase request timed out."
                };
            }

            return await tcs.Task;
        }

        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_PURCHASE_RESPONSE")
            {
                return;
            }

            PurchaseResponsePayload payload = null;
            if (!string.IsNullOrEmpty(message.payload))
            {
                payload = JsonUtility.FromJson<PurchaseResponsePayload>(message.payload);
            }

            if (payload == null || string.IsNullOrEmpty(payload.requestId))
            {
                Logger.Warn("SDK_PURCHASE_RESPONSE missing requestId");
                return;
            }

            if (!_pendingRequests.TryGetValue(payload.requestId, out var tcs))
            {
                return;
            }

            _pendingRequests.Remove(payload.requestId);
            tcs.SetResult(new PurchaseResult
            {
                Success = payload.success,
                RequestId = payload.requestId,
                WalletScope = payload.walletScope,
                Status = payload.status,
                Wallet = payload.wallet,
                PlatformWallet = payload.platformWallet,
                ErrorCode = payload.error != null ? payload.error.code : null,
                ErrorMessage = payload.error != null ? payload.error.message : null
            });
        }
    }

    [Serializable]
    public class PurchaseResult
    {
        public bool Success;
        /// <summary>Correlation id the shell echoes back. Useful for analytics and for matching
        /// a completed purchase to the request that started it.</summary>
        public string RequestId;
        public string Status;
        /// <summary>"live" or "sandbox" — which wallet the balance landed in. A
        /// sandbox purchase credits a separate test wallet that never mixes with
        /// the real one, so a game showing a balance should say which it is.</summary>
        public string WalletScope;
        public GameWalletSnapshot Wallet;
        public PlatformWalletSnapshot PlatformWallet;
        public string ErrorCode;
        public string ErrorMessage;
    }

    [Serializable]
    public class GameWalletSnapshot
    {
        public string gameId;
        public float coins;
        public float gems;
        public int version;
        // Mirrors WalletSummary.lastModified in the shell's payment service — the shell already
        // sends it, this class just was not reading it.
        public string lastModified;
    }

    [Serializable]
    public class PlatformWalletSnapshot
    {
        public string walletId;
        /// <summary>"live" or "sandbox" — which wallet this balance came from.</summary>
        public string walletScope;
        public float coins;
        public float gems;
        public int version;
        public string lastModified;
    }

    [Serializable]
    public class StoreCatalogResult
    {
        public bool Success;
        public string GameId;
        public StoreCurrencyEntry[] Currencies;
        public StoreItemEntry[] Items;
        public string Error;

        public static StoreCatalogResult Fail(string error)
        {
            return new StoreCatalogResult
            {
                Success = false,
                Error = error,
                Currencies = Array.Empty<StoreCurrencyEntry>(),
                Items = Array.Empty<StoreItemEntry>(),
            };
        }
    }

    [Serializable]
    public class PlatformWalletResult
    {
        public bool Success;
        public PlatformWalletSnapshot Wallet;
        public string Error;

        public static PlatformWalletResult Fail(string error)
        {
            return new PlatformWalletResult
            {
                Success = false,
                Error = error,
            };
        }
    }

    [Serializable]
    public class TopupPacksResult
    {
        public bool Success;
        public TopupPackEntry[] Packs;
        public string Error;

        public static TopupPacksResult Fail(string error)
        {
            return new TopupPacksResult
            {
                Success = false,
                Error = error,
                Packs = Array.Empty<TopupPackEntry>(),
            };
        }
    }

    [Serializable]
    public class InventoryResult
    {
        public bool Success;
        public StringIntEntry[] Items;
        public StringIntEntry[] Consumables;
        public string Error;

        public static InventoryResult Fail(string error)
        {
            return new InventoryResult
            {
                Success = false,
                Items = Array.Empty<StringIntEntry>(),
                Consumables = Array.Empty<StringIntEntry>(),
                Error = error,
            };
        }
    }

    [Serializable]
    public class ConsumeResult
    {
        public bool Success;
        public string ItemId;
        public int Remaining;
        public StringIntEntry[] Consumables;
        public string Error;

        public static ConsumeResult Fail(string error)
        {
            return new ConsumeResult
            {
                Success = false,
                Consumables = Array.Empty<StringIntEntry>(),
                Error = error,
            };
        }
    }

    public class PlatformWalletSpendResult
    {
        public bool Success;
        public PlatformWalletSnapshot Wallet;
        public StringIntEntry[] Inventory;
        public StringIntEntry[] Consumables;
        public string Error;

        public static PlatformWalletSpendResult Fail(string error)
        {
            return new PlatformWalletSpendResult
            {
                Success = false,
                Error = error,
                Inventory = Array.Empty<StringIntEntry>(),
                Consumables = Array.Empty<StringIntEntry>(),
            };
        }
    }

    [Serializable]
    public class StoreCurrencyEntry
    {
        public string currencyId;
        public string name;
        public string iconUrl;
        public int maxBalance;
    }

    [Serializable]
    public class StoreItemEntry
    {
        public string itemId;
        public string name;
        public string description;
        public string iconUrl;
        public string type;
        public StoreItemPrice price;
        public bool walletPurchaseEnabled;
        public bool walletPurchaseEligible;
        public bool directPurchaseEnabled;
    }

    [Serializable]
    public class StoreItemPrice
    {
        public string currency;
        public float amount;
    }

    [Serializable]
    public class TopupPackEntry
    {
        public string topupPackId;
        public string name;
        public string description;
        /// <summary>The currency the player RECEIVES — normally the platform coin.
        /// Not what the pack costs; see <see cref="priceCurrency"/>.</summary>
        public string currency;
        /// <summary>How much of <see cref="currency"/> the pack grants.</summary>
        public float amount;
        /// <summary>What the pack COSTS, in whole units of <see cref="priceCurrency"/>
        /// — 0.99 for $0.99. The server always sent this; until 1.7.0 no field
        /// existed to receive it, so a game could not show a player the price it
        /// was about to charge them.</summary>
        public float priceAmount;
        /// <summary>The same cost in minor units — 99 for $0.99. Money is held as
        /// whole minor units everywhere on the server; prefer this for any
        /// comparison or arithmetic and keep <see cref="priceAmount"/> for
        /// display, so no rounding creeps in.</summary>
        public int priceAmountMinor;
        /// <summary>ISO currency the pack is charged in, e.g. "USD".</summary>
        public string priceCurrency;
        public bool active;
        public string badge;
        public string iconUrl;
        public int sortOrder;
    }

    [Serializable]
    public class StringIntEntry
    {
        public string key;
        public int value;
    }

    [Serializable]
    class PurchaseRequestPayload
    {
        public string requestId;
        public string intent;
        public string gameId;
        public string itemId;
        public string topupPackId;
        public bool sandbox;
    }

    [Serializable]
    class PurchaseResponsePayload
    {
        public string requestId;
        public bool success;
        public string status;
        public string walletScope;
        public GameWalletSnapshot wallet;
        public PlatformWalletSnapshot platformWallet;
        public PurchaseError error;
    }

    [Serializable]
    class PurchaseError
    {
        public string code;
        public string message;
    }

    [Serializable]
    class StoreCatalogEnvelope
    {
        public bool success;
        public StoreCatalogData data;
    }

    [Serializable]
    class StoreCatalogData
    {
        public string gameId;
        public StoreCurrencyEntry[] currencyList;
        public StoreItemEntry[] items;
    }

    [Serializable]
    class TopupPacksEnvelope
    {
        public bool success;
        public TopupPacksData data;
    }

    [Serializable]
    class TopupPacksData
    {
        public TopupPackEntry[] topupPacks;
    }

    [Serializable]
    class PlatformWalletEnvelope
    {
        public bool success;
        public PlatformWalletSnapshot data;
    }

    [Serializable]
    class PlatformWalletSpendRequest
    {
        public string gameId;
        public string itemId;
        public string requestId;
    }

    [Serializable]
    class ConsumeRequest
    {
        public string gameId;
        public string itemId;
        public int quantity;
        public string requestId;
    }

    [Serializable]
    class ConsumeEnvelope
    {
        public bool success;
        public ConsumePayload data;
    }

    [Serializable]
    class ConsumePayload
    {
        public string itemId;
        public int remaining;
        public StringIntEntry[] consumablesList;
    }

    [Serializable]
    class InventoryEnvelope
    {
        public bool success;
        public InventoryPayload data;
    }

    [Serializable]
    class InventoryPayload
    {
        public string gameId;
        public StringIntEntry[] itemsList;
        public StringIntEntry[] consumablesList;
    }

    [Serializable]
    class PlatformWalletSpendEnvelope
    {
        public bool success;
        public PlatformWalletSpendData data;
    }

    [Serializable]
    class PlatformWalletSpendData
    {
        public PlatformWalletSnapshot platformWallet;
        public StringIntEntry[] inventoryList;
        public StringIntEntry[] consumablesList;
    }
}
