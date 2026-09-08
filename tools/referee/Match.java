package com.codingame.gameengine.runner;

import com.codingame.gameengine.runner.dto.GameResult;

import java.io.File;
import java.io.PrintWriter;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;
import java.util.Properties;

/**
 * Мини-арбитр: серия партий двух ботов (процессы по протоколу CodinGame) настоящим рефери Code Royale
 * без веб-сервера просмотрщика. Лежит в пакете раннера SDK ради доступа к gameResult;
 * приватный GameRunner.run() вызывается рефлексией.
 *
 * java -Dleague.level=N -cp <classpath> com.codingame.gameengine.runner.Match
 *      -p1 "<cmd>" -p2 "<cmd>" [-games N] [-seed S] [-noswap] [-v] [-dump <dir>]
 *
 * Стороны чередуются (нечётные партии — p2 первым игроком), итог печатается в терминах p1/p2.
 * Счёт игрока = HP королевы в конце, -1 = убит рефери за кривой вывод или таймаут.
 */
public class Match {
    public static void main(String[] args) throws Exception {
        int games = 1;
        long seed = 1;
        String p1 = null, p2 = null;
        boolean swap = true, verbose = false;
        String dump = null;
        for (int i = 0; i < args.length; i++) {
            switch (args[i]) {
                case "-games": games = Integer.parseInt(args[++i]); break;
                case "-seed": seed = Long.parseLong(args[++i]); break;
                case "-p1": p1 = args[++i]; break;
                case "-p2": p2 = args[++i]; break;
                case "-noswap": swap = false; break;
                case "-v": verbose = true; break;
                case "-dump": dump = args[++i]; break;
                default: throw new IllegalArgumentException("unknown arg " + args[i]);
            }
        }
        if (p1 == null || p2 == null) throw new IllegalArgumentException("-p1 and -p2 are required");
        if (dump != null) new File(dump).mkdirs();

        // start(port) = initialize(new Properties()) + run() + просмотрщик; повторяем первые два шага.
        Method init = GameRunner.class.getDeclaredMethod("initialize", Properties.class);
        init.setAccessible(true);
        Method run = GameRunner.class.getDeclaredMethod("run");
        run.setAccessible(true);

        int w1 = 0, w2 = 0, draws = 0;
        int[] warn = new int[2], kill = new int[2];
        long totalMs = 0;
        for (int g = 0; g < games; g++) {
            boolean swapped = swap && g % 2 == 1;
            String a = swapped ? p2 : p1, b = swapped ? p1 : p2;
            Properties props = new Properties();
            props.setProperty("seed", String.valueOf(seed + g));
            GameRunner runner = new GameRunner(props);
            runner.addAgent(a);
            runner.addAgent(b);
            long t0 = System.currentTimeMillis();
            init.invoke(runner, new Properties());
            run.invoke(runner);
            long ms = System.currentTimeMillis() - t0;
            totalMs += ms;
            GameResult r = runner.gameResult;

            int s0 = r.scores.get(0), s1 = r.scores.get(1);
            int sp1 = swapped ? s1 : s0, sp2 = swapped ? s0 : s1;
            String winner;
            if (sp1 > sp2) { w1++; winner = "p1"; } else if (sp2 > sp1) { w2++; winner = "p2"; } else { draws++; winner = "draw"; }

            int[] gw = new int[2], gk = new int[2];
            List<String> notable = new ArrayList<String>();
            for (String s : r.summaries) {
                if (s == null) continue;
                for (String line : s.split("\n")) {
                    int who = line.startsWith("$0") ? 0 : line.startsWith("$1") ? 1 : -1;
                    if (who < 0) continue;
                    int idx = swapped ? 1 - who : who;
                    if (line.contains("[WARNING]")) gw[idx]++;
                    else { gk[idx]++; if (notable.size() < 6) notable.add(line); }
                }
            }
            for (int i = 0; i < 2; i++) { warn[i] += gw[i]; kill[i] += gk[i]; }

            int frames = r.summaries.size();
            System.out.println(String.format("game %d seed %d: p1 %d p2 %d -> %s, %d frames, warnings %d/%d, kills %d/%d, %d ms%s",
                g, seed + g, sp1, sp2, winner, frames, gw[0], gw[1], gk[0], gk[1], ms, swapped ? " (swapped)" : ""));
            if (verbose || gk[0] + gk[1] > 0) for (String n : notable) System.out.println("   " + n);
            if (verbose && r.errors != null) {
                for (String key : r.errors.keySet()) {
                    List<String> lines = r.errors.get(key);
                    if (lines == null || lines.isEmpty()) continue;
                    int from = Math.max(0, lines.size() - 3);
                    for (int i = from; i < lines.size(); i++) if (lines.get(i) != null) System.out.println("   stderr[" + key + "] " + lines.get(i).trim());
                }
            }
            if (dump != null) {
                try (PrintWriter pw = new PrintWriter(new File(dump, "game-" + g + ".txt"), "UTF-8")) {
                    pw.println("# seed " + (seed + g) + " p1 " + (swapped ? "second" : "first") + " scores " + s0 + " " + s1);
                    pw.println("# refereeInput " + r.refereeInput);
                    for (int f = 0; f < r.summaries.size(); f++) {
                        pw.println("## frame " + f);
                        if (r.summaries.get(f) != null) pw.println(r.summaries.get(f).trim());
                        if (r.outputs != null) for (String key : r.outputs.keySet()) {
                            List<String> outs = r.outputs.get(key);
                            if (outs != null && f < outs.size() && outs.get(f) != null) pw.println("> " + key + ": " + outs.get(f).trim().replace("\n", " | "));
                        }
                    }
                }
            }
        }
        System.out.println(String.format("total: p1 %d, p2 %d, draws %d of %d; warnings %d/%d, kills %d/%d, avg %d ms/game",
            w1, w2, draws, games, warn[0], warn[1], kill[0], kill[1], games > 0 ? totalMs / games : 0));
    }
}
