//! Small synchronous ABI. Call on a worker thread; returned strings belong to Rust.
use serde::{Deserialize, Serialize};
use std::{
    ffi::{CStr, CString, c_char},
    fs,
    path::Path,
    sync::OnceLock,
};

const MAX_ITEMS: usize = 20_000;

/// Represents a discovered media item.
#[derive(Debug, Serialize, PartialEq, Clone)]
#[serde(rename_all = "camelCase")]
pub struct MediaItem {
    pub path: String,
    pub name: String,
    pub kind: &'static str,
    pub bytes: u64,
}

/// Aggregated result of a directory scan or single-file probe.
#[derive(Debug, Default, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ScanResult {
    pub items: Vec<MediaItem>,
    pub skipped: usize,
    pub truncated: bool,
    pub error: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct FormatManifest<'a> {
    #[serde(borrow)]
    images: Vec<&'a str>,
    #[serde(borrow)]
    platform_images: Vec<&'a str>,
    #[serde(borrow)]
    videos: Vec<&'a str>,
}

const FORMAT_MANIFEST: &str = include_str!("../../../formats/media-formats.json");

fn extensions() -> &'static [(&'static str, &'static str)] {
    static EXTENSIONS: OnceLock<Vec<(&'static str, &'static str)>> = OnceLock::new();
    EXTENSIONS.get_or_init(|| {
        // Checked-in data is validated by tests. Strings borrow the embedded JSON, not per-file heaps.
        let manifest: FormatManifest<'static> =
            serde_json::from_str(FORMAT_MANIFEST).expect("Invalid embedded media format manifest");
        let mut entries: Vec<_> = manifest
            .images
            .into_iter()
            .chain(manifest.platform_images)
            .map(|extension| (&extension[1..], "image"))
            .chain(
                manifest
                    .videos
                    .into_iter()
                    .map(|extension| (&extension[1..], "video")),
            )
            .collect();
        entries.sort_unstable_by_key(|entry| entry.0);
        entries
    })
}

/// Classifies recognized extensions. Actual decoding depends on the installed Windows codecs.
pub fn media_kind(path: &Path) -> Option<&'static str> {
    let ext = path.extension()?.to_str()?;
    let entries = extensions();
    entries
        .binary_search_by(|(candidate, _)| {
            candidate
                .bytes()
                .cmp(ext.bytes().map(|byte| byte.to_ascii_lowercase()))
        })
        .ok()
        .map(|index| entries[index].1)
}

/// Scans a folder non-recursively for media files up to the default maximum limit.
pub fn scan_folder(folder: &Path) -> ScanResult {
    scan_folder_with_limit(folder, MAX_ITEMS)
}

fn scan_folder_with_limit(folder: &Path, limit: usize) -> ScanResult {
    let limit = limit.min(MAX_ITEMS);
    let mut result = ScanResult::default();
    let entries = match fs::read_dir(folder) {
        Ok(entries) => entries,
        Err(error) => {
            result.error = Some(error.to_string());
            return result;
        }
    };
    // Cache one case-folded key per included name, not a second clone of every original name.
    let mut included = Vec::with_capacity(limit.min(128));
    // Non-recursive by design: never follow junctions or load entire disks.
    for entry in entries {
        let entry = match entry {
            Ok(entry) => entry,
            Err(_) => {
                result.skipped += 1;
                continue;
            }
        };
        let name = entry.file_name();
        let Some(kind) = media_kind(Path::new(&name)) else {
            continue;
        };
        let file_type = match entry.file_type() {
            Ok(file_type) => file_type,
            Err(_) => {
                result.skipped += 1;
                continue;
            }
        };
        // file_type does not follow symlinks. Avoid metadata/path allocations for directories,
        // junctions and symlinks; the catalog never traverses a media-looking link.
        if !file_type.is_file() {
            continue;
        }
        let metadata = match entry.metadata() {
            Ok(metadata) => metadata,
            Err(_) => {
                result.skipped += 1;
                continue;
            }
        };
        if !metadata.is_file() {
            continue;
        }
        if included.len() == limit {
            result.truncated = true;
            break;
        }
        let name = name.to_string_lossy().into_owned();
        included.push((
            name.to_lowercase(),
            MediaItem {
                name,
                path: entry.path().to_string_lossy().into_owned(),
                kind,
                bytes: metadata.len(),
            },
        ));
    }
    included.sort_unstable_by(|(left_key, left), (right_key, right)| {
        left_key
            .cmp(right_key)
            .then_with(|| left.name.cmp(&right.name))
    });
    result.items = included.into_iter().map(|(_, item)| item).collect();
    result
}

/// Inspect one file without enumerating its parent directory (Explorer quick preview).
pub fn probe_file(path: &Path) -> ScanResult {
    let mut result = ScanResult::default();
    let Some(kind) = media_kind(path) else {
        result.error = Some("Unsupported media extension".into());
        return result;
    };
    // As in directory scans, never resolve a symlink/reparse-point target for quick preview.
    match fs::symlink_metadata(path) {
        Ok(metadata) if metadata.is_file() => result.items.push(MediaItem {
            path: path.to_string_lossy().into_owned(),
            name: path
                .file_name()
                .unwrap_or_default()
                .to_string_lossy()
                .into_owned(),
            kind,
            bytes: metadata.len(),
        }),
        Ok(_) => result.error = Some("Path is not a file".into()),
        Err(error) => result.error = Some(error.to_string()),
    }
    result
}

/// # Safety
/// `folder_utf8` must point to a valid, NUL-terminated UTF-8 string for this call.
/// Free the return value exactly once with `ppn_string_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ppn_scan_folder(folder_utf8: *const c_char) -> *mut c_char {
    // SAFETY: caller provides a valid string as required by this export's contract.
    unsafe { call_path(folder_utf8, scan_folder) }
}

/// # Safety
/// `path_utf8` must point to a valid, NUL-terminated UTF-8 string for this call.
/// Free the return value exactly once with `ppn_string_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ppn_probe_file(path_utf8: *const c_char) -> *mut c_char {
    // SAFETY: caller provides a valid string as required by this export's contract.
    unsafe { call_path(path_utf8, probe_file) }
}

unsafe fn call_path(path_utf8: *const c_char, action: fn(&Path) -> ScanResult) -> *mut c_char {
    let json = std::panic::catch_unwind(|| {
        if path_utf8.is_null() {
            return serde_json::to_string(&ScanResult {
                error: Some("Path pointer is null".into()),
                ..Default::default()
            })
            .unwrap_or_else(|_| "{\"error\":\"Path pointer is null\"}".into());
        }
        let result = match unsafe { CStr::from_ptr(path_utf8) }.to_str() {
            Ok(path) => action(Path::new(path)),
            Err(_) => ScanResult {
                error: Some("Path is not UTF-8".into()),
                ..Default::default()
            },
        };
        serde_json::to_string(&result)
            .unwrap_or_else(|_| "{\"error\":\"Serialization failed\"}".into())
    })
    .unwrap_or_else(|_| "{\"error\":\"Native operation failed\"}".into());

    match CString::new(json) {
        Ok(c_str) => c_str.into_raw(),
        Err(_) => CString::new("{\"error\":\"Internal string conversion failed\"}")
            .unwrap_or_default()
            .into_raw(),
    }
}

/// # Safety
/// `value` must be null or an unfreed pointer returned by either path export.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ppn_string_free(value: *mut c_char) {
    if !value.is_null() {
        drop(unsafe { CString::from_raw(value) });
    }
}

/// Returns the UTF-8 byte length, excluding NUL, without a managed UTF-16 conversion.
/// # Safety
/// `value` must be null or an unfreed pointer returned by either path export.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ppn_string_length(value: *const c_char) -> usize {
    if value.is_null() {
        0
    } else {
        unsafe { CStr::from_ptr(value) }.to_bytes().len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn classifies_case_insensitive_extensions() {
        assert_eq!(media_kind(Path::new("你好.JPG")), Some("image"));
        assert_eq!(media_kind(Path::new("clip.MKV")), Some("video"));
        assert_eq!(media_kind(Path::new("file.exe")), None);
        assert_eq!(media_kind(Path::new("no-extension")), None);
    }

    #[test]
    fn recognizes_expanded_formats_without_claiming_installed_codecs() {
        for name in [
            "vector.SVG",
            "icon.ICO",
            "photo.JXR",
            "photo.HEIF",
            "photo.AVIF",
            "raw.CR3",
            "raw.NEF",
        ] {
            assert_eq!(media_kind(Path::new(name)), Some("image"));
        }
        for name in ["clip.M2TS", "clip.MTS", "clip.ASF", "clip.3GP", "clip.VOB"] {
            assert_eq!(media_kind(Path::new(name)), Some("video"));
        }
        for name in [
            "not-image.svg.exe",
            "not-image.jpg.tmp",
            "image.jрg",
            "folder.",
            "image.png ",
        ] {
            assert_eq!(media_kind(Path::new(name)), None);
        }
    }

    #[test]
    fn shared_format_manifest_has_unique_lowercase_ascii_extensions() {
        let manifest: FormatManifest<'_> = serde_json::from_str(FORMAT_MANIFEST).unwrap();
        let mut unique = std::collections::HashSet::new();
        for extension in manifest
            .images
            .into_iter()
            .chain(manifest.platform_images)
            .chain(manifest.videos)
        {
            assert!(extension.starts_with('.'));
            assert!(extension.len() > 1);
            assert!(
                extension[1..]
                    .bytes()
                    .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit())
            );
            assert!(unique.insert(extension), "Duplicate extension: {extension}");
            assert!(media_kind(Path::new(&format!("fixture{extension}"))).is_some());
        }
        assert_eq!(unique.len(), extensions().len());
    }

    #[test]
    fn media_named_directories_are_not_catalog_items_and_zero_limit_is_bounded() {
        let dir = test_directory("types");
        fs::create_dir_all(dir.join("directory.jpg")).unwrap();
        fs::write(dir.join("directory.jpg/hidden.png"), b"image").unwrap();
        fs::write(dir.join("clip.MP4"), b"video").unwrap();
        let result = scan_folder(&dir);
        assert_eq!(result.items.len(), 1);
        assert_eq!(result.items[0].name, "clip.MP4");
        let zero = scan_folder_with_limit(&dir, 0);
        assert!(zero.items.is_empty() && zero.truncated);
        assert!(probe_file(&dir.join("directory.jpg")).error.is_some());
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn deterministic_unicode_case_fold_sort_retains_original_names() {
        let dir = test_directory("sort");
        fs::create_dir_all(&dir).unwrap();
        for name in [
            "zebra.jpg",
            "Äpfel.jpg",
            "beta.png",
            "ALPHA.jpg",
            "你好.png",
        ] {
            fs::write(dir.join(name), name.as_bytes()).unwrap();
        }
        let result = scan_folder(&dir);
        let names: Vec<_> = result.items.iter().map(|item| item.name.as_str()).collect();
        assert_eq!(
            names,
            [
                "ALPHA.jpg",
                "beta.png",
                "zebra.jpg",
                "Äpfel.jpg",
                "你好.png"
            ]
        );
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn production_cap_does_not_grow_with_a_larger_requested_limit() {
        let dir = test_directory("cap");
        fs::create_dir_all(&dir).unwrap();
        for index in 0..MAX_ITEMS + 1 {
            fs::write(dir.join(format!("{index:05}.jpg")), b"").unwrap();
        }
        let result = scan_folder_with_limit(&dir, MAX_ITEMS + 100);
        assert_eq!(result.items.len(), MAX_ITEMS);
        assert!(result.truncated);
        fs::remove_dir_all(dir).unwrap();
    }

    #[cfg(any(unix, windows))]
    #[test]
    fn scans_and_probes_do_not_follow_file_symlinks() {
        let dir = test_directory("links");
        fs::create_dir_all(&dir).unwrap();
        let target = dir.join("target.jpg");
        let link = dir.join("link.jpg");
        fs::write(&target, b"image").unwrap();
        #[cfg(unix)]
        let created = std::os::unix::fs::symlink(&target, &link);
        #[cfg(windows)]
        let created = std::os::windows::fs::symlink_file(&target, &link);
        if let Err(error) = created {
            // Windows hosts may not grant symlink creation; do not change OS policy for tests.
            eprintln!("Symlink fixture unavailable: {error}");
            fs::remove_dir_all(dir).unwrap();
            return;
        }
        let result = scan_folder(&dir);
        assert_eq!(result.items.len(), 1);
        assert_eq!(result.items[0].name, "target.jpg");
        assert!(probe_file(&link).error.is_some());
        fs::remove_file(link).unwrap();
        fs::remove_dir_all(dir).unwrap();
    }

    fn test_directory(label: &str) -> std::path::PathBuf {
        std::env::temp_dir().join(format!(
            "ppn-{label}-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ))
    }

    #[test]
    fn scans_only_media_and_skips_subfolders() {
        let dir = std::env::temp_dir().join(format!(
            "ppn-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(dir.join("nested")).unwrap();
        fs::write(dir.join("你好.PNG"), b"png").unwrap();
        fs::write(dir.join("a.MP4"), b"movie").unwrap();
        fs::write(dir.join("notes.txt"), b"text").unwrap();
        fs::write(dir.join("nested/hidden.jpg"), b"image").unwrap();
        let result = scan_folder(&dir);
        assert!(result.error.is_none());
        assert_eq!(result.items.len(), 2);
        assert_eq!(result.items[0].name, "a.MP4");
        assert_eq!(result.items[1].bytes, 3);
        let limited = scan_folder_with_limit(&dir, 1);
        assert_eq!(limited.items.len(), 1);
        assert!(limited.truncated);
        assert!(!scan_folder_with_limit(&dir, 2).truncated);
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn abi_handles_null_and_releases_owned_string() {
        unsafe {
            let ptr = ppn_scan_folder(std::ptr::null());
            assert_eq!(ppn_string_length(ptr), CStr::from_ptr(ptr).to_bytes().len());
            assert_eq!(ppn_string_length(std::ptr::null()), 0);
            let json: serde_json::Value =
                serde_json::from_str(CStr::from_ptr(ptr).to_str().unwrap()).unwrap();
            assert!(json["error"].is_string());
            ppn_string_free(ptr);
            ppn_string_free(std::ptr::null_mut());
        }
    }

    #[test]
    fn missing_folder_is_an_error_not_a_panic() {
        let dir = std::env::temp_dir().join(format!("ppn-missing-{}", std::process::id()));
        assert!(scan_folder(&dir).error.is_some());
    }

    #[test]
    fn abi_rejects_invalid_utf8() {
        let invalid = [0xff_u8, 0];
        unsafe {
            let ptr = ppn_scan_folder(invalid.as_ptr().cast());
            let result: serde_json::Value =
                serde_json::from_str(CStr::from_ptr(ptr).to_str().unwrap()).unwrap();
            assert_eq!(result["error"], "Path is not UTF-8");
            ppn_string_free(ptr);
        }
    }

    #[test]
    fn probe_rejects_non_media_and_missing_files() {
        assert!(probe_file(Path::new("notes.txt")).error.is_some());
        let missing = std::env::temp_dir().join(format!("ppn-missing-{}.jpg", std::process::id()));
        assert!(probe_file(&missing).error.is_some());
    }

    #[test]
    fn probe_abi_roundtrips_unicode_file_without_enumeration() {
        let dir = std::env::temp_dir().join(format!(
            "ppn-probe-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(&dir).unwrap();
        let path = dir.join("你好 image.PNG");
        fs::write(&path, b"123456").unwrap();
        let encoded = CString::new(path.to_str().unwrap()).unwrap();
        unsafe {
            let ptr = ppn_probe_file(encoded.as_ptr());
            assert_eq!(ppn_string_length(ptr), CStr::from_ptr(ptr).to_bytes().len());
            let result: serde_json::Value =
                serde_json::from_str(CStr::from_ptr(ptr).to_str().unwrap()).unwrap();
            assert!(result["error"].is_null());
            assert_eq!(result["items"].as_array().unwrap().len(), 1);
            assert_eq!(result["items"][0]["name"], "你好 image.PNG");
            assert_eq!(result["items"][0]["bytes"], 6);
            ppn_string_free(ptr);
        }
        assert!(probe_file(&dir).error.is_some());
        fs::remove_dir_all(&dir).unwrap();
    }
}
