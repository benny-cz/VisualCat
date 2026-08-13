using Android.App;

// READ_LOGS is the only permission VisualCat declares. It is a privileged
// permission that Google Play cannot grant, so the app treats it as absent and
// reads only its own process logs unless an operator grants it over ADB. See
// docs/SUPPORT.md.
//
// WRITE_EXTERNAL_STORAGE is deliberately not declared: it applied only up to
// API 28 and the minimum supported platform is API 31, so declaring it would add
// an unreachable permission to the store listing's disclosure.
[assembly: UsesPermission(Name = global::Android.Manifest.Permission.ReadLogs)]
