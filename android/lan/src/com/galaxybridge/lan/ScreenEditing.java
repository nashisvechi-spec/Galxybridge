package com.galaxybridge.lan;

final class ScreenEditing {
    static final class Result {
        final String text;final int start,end;
        Result(String text,int start,int end) { this.text=text;this.start=start;this.end=end; }
    }
    static Result apply(String text,int start,int end,String inserted,String key) {
        start=Math.max(0,Math.min(text.length(),start<0?text.length():start));end=Math.max(start,Math.min(text.length(),end<0?start:end));
        if(start>0 && start<text.length() && Character.isLowSurrogate(text.charAt(start))) start--;
        if(end>0 && end<text.length() && Character.isLowSurrogate(text.charAt(end))) end++;
        if("selectAll".equals(key)) return new Result(text,0,text.length());
        if("left".equals(key)) { int caret=end>start?start:start>0?text.offsetByCodePoints(start,-1):0;return new Result(text,caret,caret); }
        if("right".equals(key)) { int caret=end>start?end:end<text.length()?text.offsetByCodePoints(end,1):end;return new Result(text,caret,caret); }
        if("home".equals(key)) return new Result(text,0,0);
        if("end".equals(key)) return new Result(text,text.length(),text.length());
        if("backspace".equals(key) && start==end && start>0) start=text.offsetByCodePoints(start,-1);
        if("delete".equals(key) && start==end && end<text.length()) end=text.offsetByCodePoints(end,1);
        if(inserted==null && !"backspace".equals(key) && !"delete".equals(key) && !"enter".equals(key)) return null;
        String value=inserted!=null?inserted:"enter".equals(key)?"\n":"";
        String result=text.substring(0,start)+value+text.substring(end);int caret=start+value.length();
        return new Result(result,caret,caret);
    }
}
