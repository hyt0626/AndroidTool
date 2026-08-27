namespace AndroidTool.Core;

public enum OperationMode
{
    Install,
    Uninstall,
    Launch
}

public enum TaskState
{
    Idle,
    Waiting,
    Running,
    CopyingObb,
    Succeeded,
    Failed
}
