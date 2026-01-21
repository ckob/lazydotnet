using lazydotnet.UI;

namespace lazydotnet.Core;

public interface IScreen : IKeyBindable
{
    void OnEnter();
    bool OnTick();
    Task<IScreen?> HandleInputAsync(ConsoleKeyInfo key, AppLayout layout);
    void Render(AppLayout layout, int width, int height);
}
