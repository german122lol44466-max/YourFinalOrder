using UnityEngine;
using YourFinalOrder.Core;

namespace YourFinalOrder.Menu
{
    /// <summary>
    /// Точка входа сцены меню: создаёт постоянный NetworkRoot (сеть, Steam-лобби, сессия), если его ещё нет.
    /// </summary>
    public class MenuBootstrap : MonoBehaviour
    {
        public GameObject networkRootPrefab;

        void Awake()
        {
            if (SessionManager.Instance == null && networkRootPrefab != null)
                Instantiate(networkRootPrefab).name = "NetworkRoot";

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
