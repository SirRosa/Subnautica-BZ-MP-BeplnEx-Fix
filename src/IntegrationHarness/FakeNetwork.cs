using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class FakeIdentifier
{
    public string GetIdentityId(GameObject gameObject, bool generateIfMissing = false) =>
        gameObject?.name;
}

namespace Subnautica.Events.EventArgs
{
    public sealed class StorageItemRemovingEventArgs
    {
        public string UniqueId { get; private set; }
        public TechType TechType { get; private set; }
        public string ItemId { get; private set; }
        public Pickupable Item { get; private set; }
        public bool IsAllowed { get; set; }

        public StorageItemRemovingEventArgs(string uniqueId, TechType techType, string itemId,
            Pickupable item, bool isAllowed = true)
        {
            UniqueId = uniqueId;
            TechType = techType;
            ItemId = itemId;
            Item = item;
            IsAllowed = isAllowed;
        }
    }
}
namespace Subnautica.Events.Handlers
{
    public static class Storage
    {
        public static readonly HashSet<string> ServerReservations =
            new HashSet<string>(StringComparer.Ordinal);
        public static int NetworkRequests;
        public static int ServerAccepted;
        public static bool HasStorageHandler = true;

        // Mimics server-side validation of the unique item ID. The client cannot
        // directly remove the object or declare crafting success.
        public static void OnItemRemoving(Subnautica.Events.EventArgs.StorageItemRemovingEventArgs ev)
        {
            if (!HasStorageHandler) return; // emulate missing MP processor
            NetworkRequests++;
            ev.IsAllowed = false;
            if (ev.UniqueId == "locker-a" && ev.TechType == TechType.Locker &&
                ev.Item.GetTechType() == TechType.Titanium &&
                ServerReservations.Add(ev.ItemId))
                ServerAccepted++;
        }
    }
}
