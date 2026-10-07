#ifndef POTPLAYER_NEXT_MEDIA_CORE_H
#define POTPLAYER_NEXT_MEDIA_CORE_H
#ifdef __cplusplus
extern "C" {
#endif
/* UTF-8, NUL-terminated input; JSON UTF-8 output owned by Rust.
   Returned pointers must be released exactly once with ppn_string_free.
   These are synchronous calls: run off the UI thread. Errors use the JSON error field.
   Caller must provide valid pointers; null is handled as an error. */
char *ppn_scan_folder(const char *folder_utf8);
char *ppn_probe_file(const char *path_utf8);
void ppn_string_free(char *value);
#ifdef __cplusplus
}
#endif
#endif
