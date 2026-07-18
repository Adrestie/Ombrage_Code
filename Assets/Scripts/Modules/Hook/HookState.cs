namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// États du grappin. Le tir étant instantané (raycast), <see cref="Firing"/> ne sert
	/// qu'à l'animation cosmétique très courte de la corde avant le passage en <see cref="Attached"/>.
	/// </summary>
	public enum HookState
	{
		Idle,
		Firing,
		Attached
	}
}
