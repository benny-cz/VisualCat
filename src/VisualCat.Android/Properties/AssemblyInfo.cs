using Android.App;

// READ_LOGS is a signature|privileged|development permission. Google Play cannot grant it
// through the ordinary runtime-permission flow, so Release/Play builds do not declare it by
// default. Debug (or an explicitly opted-in non-Play build) keeps the established direct-capture
// developer path. Production full-device capture streams logcat through an explicitly paired
// local Wireless debugging connection instead. See docs/SUPPORT.md.
//
// The normal INTERNET and CHANGE_WIFI_MULTICAST_STATE permissions for Wireless debugging live in
// AndroidManifest.xml so the Play permission surface has one source of truth.
//
// WRITE_EXTERNAL_STORAGE is deliberately not declared: it applied only up to API 28 and the
// minimum supported platform is API 31, so declaring it would add an unreachable permission to
// the store listing's disclosure.
#if VISUALCAT_READ_LOGS
[assembly: UsesPermission(Name = global::Android.Manifest.Permission.ReadLogs)]
#endif
