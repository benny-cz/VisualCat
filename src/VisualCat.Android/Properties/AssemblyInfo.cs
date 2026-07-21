using Android.App;

[assembly: UsesPermission(Name = global::Android.Manifest.Permission.ReadLogs)]
[assembly: UsesPermission(Name = global::Android.Manifest.Permission.WriteExternalStorage, MaxSdkVersion = 28)]
