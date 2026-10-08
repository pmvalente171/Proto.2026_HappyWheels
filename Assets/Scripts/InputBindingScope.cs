using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public sealed class InputBindingScope
{
    readonly List<Action> _unbinders = new();

    // Occurs when you start pressing a button
    public InputBindingScope OnStarted(InputAction action,
        Action<InputAction.CallbackContext> handler)
    {
        action.started += handler;
        _unbinders.Add(() => action.started -= handler); // Starts adding actions that will be unbound later
        return this;
    }
    
    public InputBindingScope OnPerformed(InputAction action,
        Action<InputAction.CallbackContext> handler)
    {
        action.performed += handler;
        _unbinders.Add(() => action.performed -= handler);
        return this;
    }

    // Occurs when you cancel an action
    public InputBindingScope OnCanceled(InputAction action,
        Action<InputAction.CallbackContext> handler)
    {
        action.canceled += handler;
        _unbinders.Add(() => action.canceled -= handler);
        return this;
    }
    
    // Helpful binders
    public InputBindingScope OnStarted(InputAction action, Action handler) 
        => OnStarted(action, handler);
    public InputBindingScope OnPerformed(InputAction action, Action handler) 
        => OnPerformed(action, handler);
    public InputBindingScope OnCancelled(InputAction action, Action handler) 
        => OnCancelled(action, handler);


    public void Dispose()
    {
        foreach (var unbind in _unbinders) unbind();
        _unbinders.Clear(); 
    }
}