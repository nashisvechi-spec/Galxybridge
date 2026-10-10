package com.galaxybridge.lan;

public final class PointerPressTest {
    private static int checks;
    private static void check(boolean value,String reason) {
        if(!value) throw new AssertionError(reason); checks++;
    }
    public static void main(String[] args) {
        PointerPress press=new PointerPress(24);
        press.begin(100,100); press.move(108,103);
        check(!press.isDrag(),"small mouse movement stays a tap on a high-density screen");
        check(press.duration(120,500)==60,"quick click injects a stationary tap");
        press.move(124,100); check(!press.isDrag(),"touch slop boundary stays a tap");
        press.move(124,101); check(press.isDrag(),"diagonal excursion beyond slop starts a drag");
        press.move(100,100); check(press.isDrag(),"returning to the starting point does not turn a drag into a click");
        check(press.duration(20,500)==100,"short drag has a minimum duration");
        check(press.duration(9000,500)==1000,"long drag is bounded");
        press.begin(200,200); check(!press.isDrag(),"new press clears previous drag state");
        check(press.duration(499,500)==60,"click before long-press threshold");
        check(press.duration(500,500)==600,"hold reaches device long-press timeout");
        check(press.duration(700,800)==60,"respects a longer configured timeout");
        check(press.duration(800,800)==900,"long click uses the configured timeout");
        PointerPress small=new PointerPress(8); small.begin(0,0); small.move(9,0);
        check(small.isDrag(),"low-density touch slop is respected");
        System.out.println("PASS: "+checks+" actual mouse press assertions");
    }
}
