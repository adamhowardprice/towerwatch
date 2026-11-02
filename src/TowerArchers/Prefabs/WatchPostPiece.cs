using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TowerArchers.Components;
using UnityEngine;

namespace TowerArchers.Prefabs
{
    /// <summary>The Hammer piece that spawns and owns one Tower Archer.</summary>
    internal static class WatchPostPiece
    {
        private const string PrefabName = "WatchPost";
        private const string BasePrefab = "wood_floor";
        private const string CostItem = "FineWood";

        private static Piece _piece;

        public static void Register()
        {
            var piece = new CustomPiece(PrefabName, BasePrefab, new PieceConfig
            {
                Name = "Watch Post",
                Description = "A platform that a Tower Archer guards.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                Requirements = new[]
                {
                    new RequirementConfig(CostItem, ModConfig.WatchPostCost.Value, 0, true),
                },
            });

            piece.PiecePrefab.AddComponent<WatchPost>();
            _piece = piece.Piece;
            PieceManager.Instance.AddPiece(piece);
        }

        public static void ApplyConfig()
        {
            // Jotunn resolves Requirements into m_resources after registration.
            if (_piece && _piece.m_resources.Length > 0)
            {
                _piece.m_resources[0].m_amount = ModConfig.WatchPostCost.Value;
            }
        }
    }
}
