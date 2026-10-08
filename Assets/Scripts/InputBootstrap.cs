using System;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class InputBootstrap : MonoBehaviour
{
    private GameInput _actions;

    public GameInput Actions => _actions ??= CreateActions();

    private GameInput CreateActions()
    {
        GameInput actions = new();
        actions.Enable(); // Switch to be actions.gameplay.enable
        return actions;
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
        _actions = null;
    }
}