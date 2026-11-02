using TowerArchers.Prefabs;
using UnityEngine;

namespace TowerArchers.Components
{
    /// <summary>Spawns one archer when a player places the piece, and despawns it when the piece breaks.</summary>
    public class WatchPost : MonoBehaviour, IPlaced
    {
        private const string ArcherPositionKey = "TowerArchers.ArcherPosition";
        private const float SpawnClearance = 0.1f;

        private ZNetView _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();

            // OnDestroy is not usable here because it also runs when an area unloads.
            // WearNTear calls m_onDestroyed only when a piece breaks or a player removes it.
            var wear = GetComponent<WearNTear>();
            if (wear)
            {
                wear.m_onDestroyed += DespawnArcher;
            }
        }

        // Player.PlacePiece calls this once, on the client that places the piece. The placement
        // ghost and an area reload do not call it, so they cannot spawn a second archer.
        public void OnPlaced()
        {
            if (!_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            var prefab = ZNetScene.instance.GetPrefab(TowerArcher.PrefabName);
            if (!prefab)
            {
                Jotunn.Logger.LogError($"Prefab {TowerArcher.PrefabName} is not registered.");
                return;
            }

            var position = GetTopCenter();
            var archer = Object.Instantiate(prefab, position, transform.rotation);

            // Vanilla CreatureSpawner links to its creature with this connection type.
            // ZDOMan restores connections when it loads a world, and a plain ZDOID value has no such repair.
            var zdo = _nview.GetZDO();
            zdo.SetConnection(ZDOExtraData.ConnectionType.Spawned, archer.GetComponent<ZNetView>().GetZDO().m_uid);
            zdo.Set(ArcherPositionKey, position);
        }

        private void DespawnArcher()
        {
            if (!_nview.IsValid())
            {
                return;
            }

            var id = _nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
            var archerZdo = id.IsNone() ? null : ZDOMan.instance.GetZDO(id);
            if (archerZdo == null)
            {
                // The archer is already dead.
                return;
            }

            // Only the owner of a ZDO can destroy it. Another peer can own the archer.
            var instance = ZNetScene.instance.FindInstance(archerZdo);
            if (instance)
            {
                instance.ClaimOwnership();
                ZNetScene.instance.Destroy(instance.gameObject);
            }
            else
            {
                archerZdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(archerZdo);
            }
        }

        private Vector3 GetTopCenter()
        {
            Physics.SyncTransforms();

            var top = transform.position.y;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                top = Mathf.Max(top, collider.bounds.max.y);
            }

            var center = transform.position;
            center.y = top + SpawnClearance;
            return center;
        }
    }
}
