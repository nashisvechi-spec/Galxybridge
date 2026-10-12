package com.galaxybridge.files;

import java.io.File;
import java.util.Locale;

/** Shared type classification for categories, placeholders and thumbnail decoding. */
enum FileKind {
    FOLDER("Папки",0xffe9ac34,""), IMAGE("Изображения",0xffe65b87,""),
    VIDEO("Видео",0xff9861de,""), AUDIO("Аудио",0xff5f81e2,""),
    DOCUMENT("Документы",0xffe5a840,""), DOWNLOAD("Загрузки",0xff43bfae,""),
    APK("Установочные файлы",0xff8eaf48,"APK"), PDF("PDF",0xffe65c61,"PDF"),
    WORD("Документы",0xff5387db,"DOC"), SHEET("Таблицы",0xff42a36b,"XLS"),
    SLIDES("Презентации",0xffe9854b,"PPT"), ARCHIVE("Архивы",0xffaa7a53,"ZIP"),
    OTHER("Файлы",0xff8a96a6,"");
    final String label,mark;
    final int color;
    FileKind(String label,int color,String mark) { this.label=label;this.color=color;this.mark=mark; }
    static FileKind of(File file) { return file.isDirectory()?FOLDER:ofName(file.getName()); }
    static FileKind ofName(String name) {
        int dot=name.lastIndexOf('.');String ext=dot<0?"":name.substring(dot+1).toLowerCase(Locale.ROOT);
        switch(ext) {
            case "jpg":case "jpeg":case "png":case "webp":case "gif":case "heic":case "heif":case "bmp":case "avif":return IMAGE;
            case "mp4":case "mkv":case "mov":case "webm":case "avi":case "3gp":case "m4v":return VIDEO;
            case "mp3":case "m4a":case "ogg":case "wav":case "flac":case "aac":case "opus":return AUDIO;
            case "pdf":return PDF;
            case "doc":case "docx":case "odt":case "rtf":return WORD;
            case "xls":case "xlsx":case "ods":case "csv":return SHEET;
            case "ppt":case "pptx":case "odp":return SLIDES;
            case "txt":case "md":case "json":case "xml":case "html":case "epub":case "fb2":return DOCUMENT;
            case "zip":case "rar":case "7z":case "tar":case "gz":return ARCHIVE;
            case "apk":case "apks":case "xapk":return APK;
            default:return OTHER;
        }
    }
    boolean document() { return this==DOCUMENT || this==PDF || this==WORD || this==SHEET || this==SLIDES; }
    boolean matches(File file) { FileKind type=of(file);return this==DOCUMENT?type.document():type==this; }
}
