namespace ChemForge.Commands
{
    /// <summary>
    /// 命令接口 - 所有 CAD 命令类的契约
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// 执行命令
        /// </summary>
        void Execute();
    }
}
