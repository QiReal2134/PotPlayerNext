//! Small synchronous ABI. Call on a worker thread; returned strings belong to Rust.
use serde::Serialize;
use std::{
    ffi::{CStr, CString, c_char},
    fs,
    path::Path,
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

/// Identifies whether the given path corresponds to a supported image or video format.
pub fn media_kind(path: &Path) -> Option<&'static str> {
    let ext = path.extension()?.to_str()?;
    const IMAGES: &[&str] = &[
        "jpg", "jpeg", "png", "bmp", "gif", "tif", "tiff", "webp", "heic", "avif",
    ];
    const VIDEOS: &[&str] = &[
        "mp4", "m4v", "mkv", "mov", "avi", "wmv", "webm", "mpeg", "mpg", "ts",
    ];
    if IMAGES.iter().any(|&img| ext.eq_ignore_ascii_case(img)) {
        Some("image")
    } else if VIDEOS.iter().any(|&vid| ext.eq_ignore_ascii_case(vid)) {
        Some("video")
    } else {
        None
    }
}

/// Scans a folder non-recursively for media files up to the default maximum limit.
pub fn scan_folder(folder: &Path) -> ScanResult {
    scan_folder_with_limit(folder, MAX_ITEMS)
}

fn scan_folder_with_limit(folder: &Path, limit: usize) -> ScanResult {
    let mut result = ScanResult::default();
    let entries = match fs::read_dir(folder) {
        Ok(entries) => entries,
        Err(error) => {
            result.error = Some(error.to_string());
            return result;
        }
    };
    // Non-recursive by design: never follow junctions or load entire disks.
    for entry in entries {
        let entry = match entry {
            Ok(entry) => entry,
            Err(_) => {
                result.skipped += 1;
                continue;
            }
        };
        let path = entry.path();
        let Some(kind) = media_kind(&path) else {
            continue;
        };
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
        if result.items.len() == limit {
            result.truncated = true;
            break;
        }
        result.items.push(MediaItem {
            name: entry.file_name().to_string_lossy().into_owned(),
            path: path.to_string_lossy().into_owned(),
            kind,
            bytes: metadata.len(),
        });
    }
    result
        .items
        .sort_by_cached_key(|item| (item.name.to_lowercase(), item.name.clone()));
    result
}

/// Inspect one file without enumerating its parent directory (Explorer quick preview).
pub fn probe_file(path: &Path) -> ScanResult {
    let mut result = ScanResult::default();
    let Some(kind) = media_kind(path) else {
        result.error = Some("Unsupported media extension".into());
        return result;
    };
    match fs::metadata(path) {
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
