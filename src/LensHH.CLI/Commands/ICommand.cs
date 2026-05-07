namespace LensHH.CLI.Commands
{
    public interface ICommand
    {
        string Name { get; }
        string Description { get; }
        string Help { get; }
        void Execute(Session session, string[] args);
    }
}
