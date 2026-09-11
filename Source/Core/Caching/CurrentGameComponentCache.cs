using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 当前 Game 实例范围内的 GameComponent 泛型缓存。
    /// 每个泛型闭包拥有独立静态字段，缓存键隐含组件类型；
    /// 只缓存一个 Game 实例的结果，切换新游戏或读取其他存档后按新 Game 重新解析。
    /// 不扫描 Game.components，也不注册任何 Harmony 生命周期补丁。
    /// </summary>
    internal static class CurrentGameComponentCache<T>
        where T : GameComponent
    {
        private static Game? cachedGame;
        private static T? cachedComponent;

        internal static T? Get()
        {
            Game? game = Current.Game;
            if (game == null)
            {
                cachedGame = null;
                cachedComponent = null;
                return null;
            }

            if (!ReferenceEquals(cachedGame, game))
            {
                cachedGame = game;
                cachedComponent = game.GetComponent<T>();
            }
            else if (cachedComponent == null)
            {
                // 兼容游戏初始化早期组件尚未可取的情况。
                cachedComponent = game.GetComponent<T>();
            }

            return cachedComponent;
        }
    }
}
