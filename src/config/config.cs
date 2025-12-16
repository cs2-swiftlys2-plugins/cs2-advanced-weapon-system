namespace AdvancedWeaponSystem;

public sealed class Config
{
    public Dictionary<string, WeaponData> WeaponDatas { get; set; } = [];

    public sealed class WeaponData
    {
        public int? Clip { get; set; }
        public int? Ammo { get; set; }
        public bool? BlockUsing { get; set; }
        public bool? IgnorePickUpFromBlockUsing { get; set; }
        public bool? ReloadAfterShoot { get; set; }
        public bool? UnlimitedAmmo { get; set; }
        public bool? UnlimitedClip { get; set; }
        public bool? OnlyHeadshot { get; set; }
        public string[] AdminFlagsToIgnoreBlockUsing { get; set; } = [];
        public Dictionary<int, int> WeaponQuota { get; set; } = [];
        public Dictionary<string, Dictionary<int, int>> MapSpecificQuota { get; set; } = [];
        public string? Damage { get; set; }

        private (char Op, int Val)? _cachedDamage;

        public (char Op, int Val)? GetParsedDamage()
        {
            if (_cachedDamage.HasValue)
                return _cachedDamage.Value;

            if (string.IsNullOrEmpty(Damage) || Damage.Length < 2)
                return null;

            char op = Damage[0];

            if (op != '+' && op != '-' && op != '*' && op != '/' && op != '=')
                return null;

            string numPart = Damage[1..];

            if (!int.TryParse(numPart, out int val))
                return null;

            _cachedDamage = (op, val);
            return _cachedDamage;
        }
    }
}