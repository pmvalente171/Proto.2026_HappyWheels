using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Collects input action subscriptions so they can all be removed with one <see cref="Dispose"/> call
/// (typically in OnDisable).
/// </summary>
public sealed class InputBindingScope : IDisposable
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

    // Helpful binders for handlers that don't need the callback context.
    // The wrapping lambda is stored by the overloads above, so unbinding still works.
    public InputBindingScope OnStarted(InputAction action, Action handler)
        => OnStarted(action, _ => handler());
    public InputBindingScope OnPerformed(InputAction action, Action handler)
        => OnPerformed(action, _ => handler());
    public InputBindingScope OnCanceled(InputAction action, Action handler)
        => OnCanceled(action, _ => handler());

    public void Dispose()
    {
        foreach (var unbind in _unbinders) unbind();
        _unbinders.Clear();
    }
}
