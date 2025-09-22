using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Text;
namespace SmartFleet.Common
{
    // Simple in-memory idempotency store for demo purposes
    public static class IdempotencyStore
    {
        private static ConcurrentDictionary<string, DateTime> _store = new();
        public static bool TryAdd(string id, TimeSpan ttl)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return _store.TryAdd(id, DateTime.UtcNow + ttl);
        }
        public static void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _store)
            {
                if (kv.Value < now)
                {
                    _store.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}
