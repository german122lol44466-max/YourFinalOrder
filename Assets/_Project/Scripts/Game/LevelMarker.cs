using System.Collections.Generic;
using UnityEngine;

namespace YourFinalOrder.Game
{
    /// <summary>
    /// Точка уровня: спавн игроков, монстра, предохранителей или зона выхода.
    /// </summary>
    public class LevelMarker : MonoBehaviour
    {
        public enum MarkerType { PlayerSpawn, MonsterSpawn, FuseSpawn, Exit }

        public MarkerType type;

        static readonly List<LevelMarker> all = new();

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static List<LevelMarker> GetAll(MarkerType type)
        {
            var result = new List<LevelMarker>();
            foreach (var m in all)
                if (m.type == type) result.Add(m);
            // Стабильный порядок на сервере и клиентах
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        public static LevelMarker GetFirst(MarkerType type)
        {
            var list = GetAll(type);
            return list.Count > 0 ? list[0] : null;
        }

        public static LevelMarker GetPlayerSpawn(int index)
        {
            var list = GetAll(MarkerType.PlayerSpawn);
            return list.Count > 0 ? list[Mathf.Abs(index) % list.Count] : null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = type switch
            {
                MarkerType.PlayerSpawn => Color.cyan,
                MarkerType.MonsterSpawn => Color.red,
                MarkerType.FuseSpawn => Color.yellow,
                _ => Color.green,
            };
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + transform.forward);
        }
    }
}
