using System.Collections.Generic;
using TowerArchers.Prefabs;
using UnityEngine;

namespace TowerArchers.Components
{
    /// <summary>Makes a Tower Archer friendly, and holds it at its patrol point.</summary>
    public class TowerArcherGuard : MonoBehaviour
    {
        private const float ArrivalDistance = 0.25f;

        private static readonly HashSet<TowerArcherGuard> Active = new HashSet<TowerArcherGuard>();

        private Humanoid _humanoid;
        private MonsterAI _ai;
        private ZNetView _nview;

        private void Awake()
        {
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<MonsterAI>();
            _nview = GetComponent<ZNetView>();
            Active.Add(this);
        }

        private void OnDestroy()
        {
            Active.Remove(this);
        }

        // Start, not Awake: the Character and BaseAI Awake methods must run first.
        private void Start()
        {
            if (!_nview.IsValid())
            {
                return;
            }

            if (_nview.IsOwner())
            {
                _humanoid.SetTamed(true);

                // The ZDO stores the patrol point. An area reload must not move it.
                if (!_ai.GetPatrolPoint(out _))
                {
                    _ai.SetPatrolPoint();
                }
            }

            ApplyConfig();
        }

        public static void ApplyConfigToActive()
        {
            foreach (var guard in Active)
            {
                guard.ApplyConfig();
            }
        }

        /// <summary>Overrides the movement that MonsterAI chose this frame. The AI owner calls this.</summary>
        public void Hold()
        {
            if (!_ai.GetPatrolPoint(out var anchor))
            {
                return;
            }

            var offset = anchor - transform.position;
            if (offset.magnitude > ModConfig.LeashDistance.Value)
            {
                _humanoid.m_body.position = anchor;
                _humanoid.m_body.linearVelocity = Vector3.zero;
                transform.position = anchor;
                _ai.StopMoving();
                return;
            }

            offset.y = 0f;
            if (offset.magnitude < ArrivalDistance)
            {
                _ai.StopMoving();
            }
            else
            {
                _ai.MoveTowards(offset.normalized, run: false);
            }
        }

        private void ApplyConfig()
        {
            if (!_nview.IsValid())
            {
                return;
            }

            TowerArcher.ApplyConfig(_humanoid, _ai);
            foreach (var item in _humanoid.GetInventory().GetAllItems())
            {
                ArcherBow.ApplyConfig(item.m_shared);
            }

            if (_nview.IsOwner())
            {
                _humanoid.SetMaxHealth(ModConfig.ArcherHealth.Value);
            }
        }
    }
}
