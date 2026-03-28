namespace slate.CodeGen
{
    public enum MCPlatform
	{
		Java,
		Bedrock,
	}

	public class MCVersion
	{
		public static readonly MCVersion LatestJava = new(MCPlatform.Java, 21, 9);
		public static readonly MCVersion LatestBedrock = new(MCPlatform.Bedrock, 23, 0);

		public MCPlatform Platform;
		public int Major;
		public int Minor;

		public override string ToString() => $"{Platform}/1.{Major}.{Minor}";

        public override bool Equals(object? obj)
        {
            return obj is MCVersion version && this == version;
        }
		public override int GetHashCode()
		{
			return HashCode.Combine(Platform, Major, Minor);
		}

		public static bool operator ==(MCVersion left, MCVersion right)
		{
			if (left is null && right is null)
				return true;
			if (left is null || right is null)
				return false;
			return left.Platform == right.Platform && left.Major == right.Major && left.Minor == right.Minor;
		}
		public static bool operator !=(MCVersion left, MCVersion right)
		{
			return !(left == right);
		}
		public static bool operator >(MCVersion left, MCVersion right)
		{
			return left.Major > right.Major || (left.Major == right.Major && left.Minor > right.Minor);
		}
		public static bool operator <(MCVersion left, MCVersion right)
		{
			return left.Major < right.Major || (left.Major == right.Major && left.Minor < right.Minor);
		}
		public static bool operator >=(MCVersion left, MCVersion right)
		{
			return left.Major > right.Major || (left.Major == right.Major && left.Minor >= right.Minor);
		}
		public static bool operator <=(MCVersion left, MCVersion right)
		{
			return left.Major < right.Major || (left.Major == right.Major && left.Minor <= right.Minor);
		}

		public static MCVersion? ParseMCVersion(string? version)
		{
			if (version is null)
				return LatestJava;

			const string javaPrefix = "java";
			const string bedrockPrefix = "bedrock";

			var parts = version.Split('/');
			if (parts.Length != 2)
				return null;
			MCPlatform? platform = parts[0] == javaPrefix 
				? MCPlatform.Java 
				: parts[0] == bedrockPrefix
				? MCPlatform.Bedrock
				: null;
			if (platform is null)
				return null;
			var versionParts = parts[1].Split('.');
			if (versionParts.Length != 3)
				return null;
			if (!int.TryParse(versionParts[0], out var major))
				return null;
			if (!int.TryParse(versionParts[1], out var minor))
				return null;

			return new MCVersion(platform.Value, major, minor);
		}

		public MCVersion(MCPlatform platform, int major, int minor)
		{
			Platform = platform;
			Major = major;
			Minor = minor;
		}
	}
}