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
        private readonly Dictionary<string, TaskCompletionSource<XsollaPurchaseResult>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<XsollaPurchaseResult>>();

        public PurchasesModule(HttpClient httpClient, string gameId)
        {
            _httpClient = httpClient;
            _gameId = gameId;
        }

        public void Initialize()
        {
            WebGLBridge.OnMessageReceived += HandleMessage;
        }

        public Task<XsollaPurchaseResult> StartXsollaPurchase(bool sandbox = false)
        {
            return StartDirectItemPurchase(null, sandbox);
        }

        public Task<XsollaPurchaseResult> StartDirectItemPurchase(string itemId, bool sandbox = false)
        {
            return StartXsollaPurchaseInternal("direct_item", itemId, null, sandbox);
        }

        public Task<XsollaPurchaseResult> StartWalletTopupPurchase(string topupPackId, bool sandbox = false)
        {
            if (string.IsNullOrEmpty(topupPackId))
            {
                return Task.FromResult(new XsollaPurchaseResult
                {
                    Success = false,
                    Status = "invalid-request",
                    ErrorCode = "INVALID_ARGUMENT",
                    ErrorMessage = "topupPackId is required."
                });
            }

            return StartXsollaPurchaseInternal("wallet_topup", null, topupPackId, sandbox);
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

        public async Task<PlatformWalletSpendResult> SpendPlatformWallet(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return PlatformWalletSpendResult.Fail("itemId is required.");
            }

            var body = JsonUtility.ToJson(new PlatformWalletSpendRequest
            {
                gameId = _gameId,
                itemId = itemId,
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

        private async Task<XsollaPurchaseResult> StartXsollaPurchaseInternal(
            string intent,
            string itemId,
            string topupPackId,
            bool sandbox
        )
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<XsollaPurchaseResult>();
            _pendingRequests[requestId] = tcs;

            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_XSOLLA_PURCHASE",
                payload = JsonUtility.ToJson(
                    new XsollaPurchaseRequestPayload
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
                return new XsollaPurchaseResult
                {
                    Success = false,
                    Status = "timeout",
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = "Xsolla purchase request timed out."
                };
            }

            return await tcs.Task;
        }

        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_XSOLLA_PURCHASE_RESPONSE")
            {
                return;
            }

            XsollaPurchaseResponsePayload payload = null;
            if (!string.IsNullOrEmpty(message.payload))
            {
                payload = JsonUtility.FromJson<XsollaPurchaseResponsePayload>(message.payload);
            }

            if (payload == null || string.IsNullOrEmpty(payload.requestId))
            {
                Logger.Warn("SDK_XSOLLA_PURCHASE_RESPONSE missing requestId");
                return;
            }

            if (!_pendingRequests.TryGetValue(payload.requestId, out var tcs))
            {
                return;
            }

            _pendingRequests.Remove(payload.requestId);
            tcs.SetResult(new XsollaPurchaseResult
            {
                Success = payload.success,
                Status = payload.status,
                Wallet = payload.wallet,
                PlatformWallet = payload.platformWallet,
                ErrorCode = payload.error != null ? payload.error.code : null,
                ErrorMessage = payload.error != null ? payload.error.message : null
            });
        }
    }

    [Serializable]
    public class XsollaPurchaseResult
    {
        public bool Success;
        public string Status;
        public XsollaWalletSnapshot Wallet;
        public PlatformWalletSnapshot PlatformWallet;
        public string ErrorCode;
        public string ErrorMessage;
    }

    [Serializable]
    public class XsollaWalletSnapshot
    {
        public string gameId;
        public float coins;
        public float gems;
        public int version;
    }

    [Serializable]
    public class PlatformWalletSnapshot
    {
        public string walletId;
        public float coins;
        public float gems;
        public int version;
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
        public string xsollaSku;
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
        public string currency;
        public float amount;
        public string xsollaSku;
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
    class XsollaPurchaseRequestPayload
    {
        public string requestId;
        public string intent;
        public string gameId;
        public string itemId;
        public string topupPackId;
        public bool sandbox;
    }

    [Serializable]
    class XsollaPurchaseResponsePayload
    {
        public string requestId;
        public bool success;
        public string status;
        public XsollaWalletSnapshot wallet;
        public PlatformWalletSnapshot platformWallet;
        public XsollaPurchaseError error;
    }

    [Serializable]
    class XsollaPurchaseError
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
