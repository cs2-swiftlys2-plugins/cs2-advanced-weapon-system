using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using System;
using System.Reflection;
using Tomlyn.Extensions.Configuration;
using static AdvancedWeaponSystem.Config;
using static Dapper.SqlMapper;

namespace AdvancedWeaponSystem;

[PluginMetadata(Id = "AdvancedWeaponSystem", Version = "v1", Name = "AdvancedWeaponSystem", Author = "schwarper")]
public sealed class AdvancedWeaponSystem(ISwiftlyCore core) : BasePlugin(core)
{
    public static ISwiftlyCore Instance { get; set; } = null!;
    public IOptionsMonitor<Config> ConfigMonitor { get; set; } = null!;

    public override void Load(bool hotReload)
    {
        Instance = Core;

        const string ConfigFileName = "config.toml";
        const string ConfigSection = "AdvancedWeaponSystem";

        Core.Configuration
            .InitializeTomlWithModel<Config>(ConfigFileName, ConfigSection)
            .Configure(cfg => cfg.AddTomlFile(ConfigFileName, optional: false, reloadOnChange: true));

        ServiceCollection services = new();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<Config>()
            .BindConfiguration("Main");

        ConfigMonitor = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<Config>>();

        Core.GameHooks.Entities.TakeDamage.Pre += OnEntityTakeDamage;
        Core.GameHooks.Items.CanAcquire.Pre += OnItemCanAcquire;
    }

    public override void Unload()
    {
        Core.GameHooks.Entities.TakeDamage.Pre -= OnEntityTakeDamage;
        Core.GameHooks.Items.CanAcquire.Pre -= OnItemCanAcquire;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnWeaponFire(EventWeaponFire @event)
    {
        if (@event.UserIdPlayer is not { } player ||
            player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value is not { } activeWeapon)
            return HookResult.Continue;

        if (!ConfigMonitor.CurrentValue.WeaponDatas.TryGetValue(activeWeapon.GetDesignerName(), out WeaponData? weaponData))
            return HookResult.Continue;

        if (weaponData.UnlimitedClip == true)
        {
            activeWeapon.Clip1 += 1;
            activeWeapon.Clip1Updated();
        }

        if (weaponData.UnlimitedAmmo == true)
        {
            activeWeapon.ReserveAmmo[0] += 1;
            activeWeapon.ReserveAmmoUpdated();
        }

        if (weaponData.ReloadAfterShoot == true)
        {
            var otherWeapon = player.PlayerPawn.WeaponServices.MyWeapons
                .FirstOrDefault(w => w.Value != activeWeapon).Value;

            if (otherWeapon != null)
            {
                Core.Scheduler.DelayBySeconds(0.1f, () => player.PlayerPawn.WeaponServices.SelectWeapon(otherWeapon));
                Core.Scheduler.DelayBySeconds(0.2f, () => player.PlayerPawn.WeaponServices.SelectWeapon(activeWeapon));
            }
        }

        return HookResult.Continue;
    }

    [EventListener<EventDelegates.OnEntitySpawned>]
    public void OnEntitySpawned(IOnEntitySpawnedEvent @event)
    {
        var entity = @event.Entity;
        if (!entity.DesignerName.StartsWith("weapon_"))
            return;

        if (!ConfigMonitor.CurrentValue.WeaponDatas.TryGetValue(entity.As<CBasePlayerWeapon>().GetDesignerName(), out WeaponData? weaponData))
            return;

        var weaponVData = entity.As<CCSWeaponBase>().WeaponBaseVData;

        if (weaponData.Clip.HasValue)
            weaponVData.MaxClip1 = weaponData.Clip.Value;

        if (weaponData.Ammo.HasValue)
            weaponVData.PrimaryReserveAmmoMax = weaponData.Ammo.Value;
    }

    private void OnEntityTakeDamage(ref TakeDamageEntityPreContext ctx)
    {
        var player = ctx.Params.Entity;
        if (player.DesignerName is not "player")
            return;

        ref CTakeDamageInfo info = ref ctx.Params.Info;
        var weapon = info.Ability.Value;
        if (weapon == null)
            return;

        if (!ConfigMonitor.CurrentValue.WeaponDatas.TryGetValue(weapon.As<CBasePlayerWeapon>().GetDesignerName(), out WeaponData? weaponData))
            return;

        if (weaponData.OnlyHeadshot == true && info.ActualHitGroup != HitGroup_t.HITGROUP_HEAD)
        {
            ctx.SetHookResult(HookResult.Stop);
            return;
        }

        var parsedDamage = weaponData.GetParsedDamage();

        if (!parsedDamage.HasValue)
            return;

        float oldDamage = info.Damage;
        int value = parsedDamage.Value.Val;
        char operation = parsedDamage.Value.Op;

        float newDamage = operation switch
        {
            '=' => value,
            '+' => oldDamage + value,
            '-' => oldDamage - value,
            '*' => oldDamage * value,
            '/' => value != 0 ? oldDamage / value : oldDamage,
            _ => oldDamage
        };

        info.Damage = newDamage;
        info.DamageCustom = (uint)newDamage;
        info.OriginalDamage = newDamage;
        info.TotalledDamage = newDamage;
    }

    private void OnItemCanAcquire(ref CanAcquireItemPreContext ctx)
    {
        if (ctx.Params.Player is not { } player)
            return;

        if (Core.Helpers.GetClassnameByDefinitionIndex(ctx.Params.EconItemView.ItemDefinitionIndex) is not { } weaponName)
            return;

        if (!ConfigMonitor.CurrentValue.WeaponDatas.TryGetValue(weaponName, out WeaponData? weaponData))
            return;

        if (!Weapon.IsRestricted(player, weaponName, weaponData, ctx.Params.AcquireMethod))
            return;

        if (!player.IsFakeClient)
            player.SendAlert(Instance.Translation.GetPlayerLocalizer(player)["You cannot use this weapon", weaponName]);

        ctx.SetReturn(AcquireResult.NotAllowedByProhibition);
        ctx.SetHookResult(HookResult.Stop);
    }
}