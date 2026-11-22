;; Original synthetic fixture: all filesystem calls use ordinary WASI imports.
(module
  (import "wasi_snapshot_preview1" "path_open" (func $open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "fd_read" (func $read (param i32 i32 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "fd_write" (func $write (param i32 i32 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "fd_close" (func $close (param i32) (result i32)))
  (import "wasi_snapshot_preview1" "path_unlink_file" (func $unlink (param i32 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "path_rename" (func $rename (param i32 i32 i32 i32 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "path_create_directory" (func $mkdir (param i32 i32 i32) (result i32)))
  (import "wasi_snapshot_preview1" "fd_readdir" (func $readdir (param i32 i32 i32 i64 i32) (result i32)))
  (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
  (import "extism:host/env" "store_u8" (func $store (param i64 i32)))
  (import "extism:host/env" "output_set" (func $output (param i64 i64)))
  (memory (export "memory") 1)
  (data (i32.const 0) "large.bin")
  (data (i32.const 32) "replace.txt")
  (data (i32.const 64) "delete.txt")
  (data (i32.const 96) "new.txt")
  (data (i32.const 128) "renamed.txt")
  (data (i32.const 160) "folder")
  (data (i32.const 192) "folder/nested.txt")
  (data (i32.const 224) "../outside.txt")
  (data (i32.const 256) "CREATED")
  (data (i32.const 288) "NEW")
  (data (i32.const 320) "PART")
  (data (i32.const 352) "denied")
  (data (i32.const 384) "escaped.txt")
  (func $check (param $error i32) local.get $error if unreachable end)
  (func $success (local $p i64)
    i64.const 2 call $alloc local.tee $p i32.const 79 call $store
    local.get $p i64.const 1 i64.add i32.const 75 call $store
    local.get $p i64.const 2 call $output)
  (func $copyout (param $base i32) (param $size i32) (local $p i64) (local $i i32)
    local.get $size i64.extend_i32_u call $alloc local.set $p
    (block $done (loop $copy
      local.get $i local.get $size i32.ge_u br_if $done
      local.get $p local.get $i i64.extend_i32_u i64.add
      local.get $base local.get $i i32.add i32.load8_u call $store
      local.get $i i32.const 1 i32.add local.set $i br $copy))
    local.get $p local.get $size i64.extend_i32_u call $output)
  (func $openfile (param $path i32) (param $len i32) (param $flags i32) (param $rights i64) (result i32)
    i32.const 3 i32.const 0 local.get $path local.get $len local.get $flags local.get $rights i64.const 0 i32.const 0 i32.const 512 call $open call $check
    i32.const 512 i32.load)
  (func $put (param $path i32) (param $len i32) (param $flags i32) (param $data i32) (param $size i32) (local $fd i32)
    local.get $path local.get $len local.get $flags i64.const 64 call $openfile local.set $fd
    i32.const 528 local.get $data i32.store i32.const 532 local.get $size i32.store
    local.get $fd i32.const 528 i32.const 1 i32.const 540 call $write call $check
    local.get $fd call $close call $check)
  (func (export "read_large") (result i32) (local $fd i32)
    i32.const 0 i32.const 9 i32.const 0 i64.const 2 call $openfile local.set $fd
    i32.const 528 i32.const 1024 i32.store i32.const 532 i32.const 5 i32.store
    local.get $fd i32.const 528 i32.const 1 i32.const 540 call $read call $check
    i32.const 540 i32.load i32.const 5 i32.ne if unreachable end
    i32.const 1024 i32.load8_u i32.const 76 i32.ne if unreachable end
    local.get $fd call $close call $check
    call $success i32.const 0)
  (func (export "mutate") (result i32)
    ;; Full overwrite must stage three bytes without copying the old contents.
    i32.const 32 i32.const 11 i32.const 8 i32.const 288 i32.const 3 call $put
    i32.const 96 i32.const 7 i32.const 1 i32.const 256 i32.const 7 call $put
    i32.const 3 i32.const 96 i32.const 7 i32.const 3 i32.const 128 i32.const 11 call $rename call $check
    i32.const 3 i32.const 64 i32.const 10 call $unlink call $check
    i32.const 3 i32.const 160 i32.const 6 call $mkdir call $check
    i32.const 192 i32.const 17 i32.const 1 i32.const 256 i32.const 7 call $put
    call $success i32.const 0)
  (func (export "partial") (result i32)
    ;; Partial write must copy up just the affected file and preserve its suffix.
    i32.const 64 i32.const 10 i32.const 0 i32.const 320 i32.const 4 call $put
    call $success i32.const 0)
  (func (export "list") (result i32)
    i32.const 3 i32.const 4096 i32.const 4096 i64.const 0 i32.const 540 call $readdir call $check
    i32.const 540 i32.load i32.eqz if unreachable end
    i32.const 4096 i32.const 540 i32.load call $copyout i32.const 0)
  (func (export "escape") (result i32)
    i32.const 3 i32.const 0 i32.const 224 i32.const 14 i32.const 1 i64.const 64 i64.const 0 i32.const 0 i32.const 512 call $open i32.eqz if unreachable end
    call $success i32.const 0)
  (func (export "cancel_loop") (result i32)
    i32.const 96 i32.const 7 i32.const 1 i32.const 256 i32.const 7 call $put
    (loop $forever br $forever)
    i32.const 0)
)
