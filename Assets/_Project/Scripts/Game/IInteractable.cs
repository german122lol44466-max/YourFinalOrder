using YourFinalOrder.Player;

namespace YourFinalOrder.Game
{
    /// <summary>
    /// Объект, с которым игрок может взаимодействовать клавишей E.
    /// ServerInteract вызывается только на сервере после проверки дистанции.
    /// </summary>
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract { get; }
        void ServerInteract(PlayerInteractor by);
    }
}
