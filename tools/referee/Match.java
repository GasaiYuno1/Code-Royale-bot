package com.codingame.gameengine.runner;

import com.codingame.gameengine.runner.dto.GameResult;

import java.io.File;
import java.io.PrintWriter;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;
import java.util.Properties;
import java.lang.reflect.Field;

/**
 * Мини-арбитр: серия партий двух ботов (процессы по протоколу CodinGame) настоящим рефери Code Royale
 * без веб-сервера просмотрщика. Лежит в пакете раннера SDK ради доступа к gameResult;
 * приватный GameRunner.run() вызывается рефлексией.
 *
 * java -Dleague.level=N -cp <classpath> com.codingame.gameengine.runner.Match
 *      -p1 "<cmd>" -p2 "<cmd>" [-games N] [-seed S] [-noswap] [-v] [-dump <dir>] [-log <dir>]
 *
 * -log пишет полный лог партии для сверки симулятора: для каждого кадра ввод игрока как есть
 * (первый кадр включает стартовые строки) и его две строки ответа; см. формат в tests (Replay).
 *
 * Стороны чередуются (нечётные партии — p2 первым игроком), итог печатается в терминах p1/p2.
 * Счёт игрока = HP королевы в конце, -1 = убит рефери за кривой вывод или таймаут.
 */
public class Match {
    /** Агент-процесс SDK с записью всего, что рефери ему отправил: раннер пишет ввод в поток getInputStream(). */
    static class RecordingAgent extends CommandLinePlayerAgent {
        final java.io.ByteArrayOutputStream recorded = new java.io.ByteArrayOutputStream();
        private java.io.OutputStream tee;
        RecordingAgent(String cmd) { super(cmd); }
        @Override protected java.io.OutputStream getInputStream() {
            if (tee == null) {
                final java.io.OutputStream inner = super.getInputStream();
                tee = new java.io.OutputStream() {
                    @Override public void write(int b) throws java.io.IOException { recorded.write(b); inner.write(b); }
                    @Override public void write(byte[] b, int off, int len) throws java.io.IOException { recorded.write(b, off, len); inner.write(b, off, len); }
                    @Override public void flush() throws java.io.IOException { inner.flush(); }
                    @Override public void close() throws java.io.IOException { inner.close(); }
                };
            }
            return tee;
        }

        /** Режет записанный поток по протоколу: [0] — стартовый блок (numSites + сайты), дальше по блоку на ход. */
        List<String> blocks() {
            String text;
            try { text = recorded.toString("UTF-8"); } catch (java.io.UnsupportedEncodingException e) { throw new RuntimeException(e); }
            String[] lines = text.split("\n");
            List<String> out = new ArrayList<String>();
            int pos = 0;
            if (lines.length == 0 || lines[0].trim().isEmpty()) return out;
            int numSites = Integer.parseInt(lines[0].trim());
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i <= numSites && pos < lines.length; i++, pos++) sb.append(lines[pos]).append('\n');
            out.add(sb.toString());
            while (pos < lines.length && !lines[pos].trim().isEmpty()) {
                sb = new StringBuilder();
                int need = 1 + numSites + 1;
                int units = -1;
                for (int i = 0; i < need && pos < lines.length; i++, pos++) {
                    sb.append(lines[pos]).append('\n');
                    if (i == need - 1) units = Integer.parseInt(lines[pos].trim());
                }
                if (units < 0) break;
                for (int i = 0; i < units && pos < lines.length; i++, pos++) sb.append(lines[pos]).append('\n');
                out.add(sb.toString());
            }
            return out;
        }
    }

    public static void main(String[] args) throws Exception {
        int games = 1;
        long seed = 1;
        String p1 = null, p2 = null;
        boolean swap = true, verbose = false;
        String dump = null, log = null;
        for (int i = 0; i < args.length; i++) {
            switch (args[i]) {
                case "-games": games = Integer.parseInt(args[++i]); break;
                case "-seed": seed = Long.parseLong(args[++i]); break;
                case "-p1": p1 = args[++i]; break;
                case "-p2": p2 = args[++i]; break;
                case "-noswap": swap = false; break;
                case "-v": verbose = true; break;
                case "-dump": dump = args[++i]; break;
                case "-log": log = args[++i]; break;
                default: throw new IllegalArgumentException("unknown arg " + args[i]);
            }
        }
        if (p1 == null || p2 == null) throw new IllegalArgumentException("-p1 and -p2 are required");
        if (dump != null) new File(dump).mkdirs();
        if (log != null) new File(log).mkdirs();

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
            RecordingAgent[] agents = { new RecordingAgent(a), new RecordingAgent(b) };
            Method add = GameRunner.class.getDeclaredMethod("addAgent", Agent.class, String.class, String.class);
            add.setAccessible(true);
            add.invoke(runner, agents[0], "Player 0", "16085713250612");
            add.invoke(runner, agents[1], "Player 1", "16085756802960");
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
            if (log != null) {
                try (PrintWriter pw = new PrintWriter(new File(log, "game-" + (seed + g) + ".log"), "UTF-8")) {
                    pw.println("# game seed=" + (seed + g) + " league=" + System.getProperty("league.level", "1") + " p0=" + a + " p1=" + b);
                    List<String>[] blocks = new List[] { agents[0].blocks(), agents[1].blocks() };
                    for (int p = 0; p < 2; p++) {
                        pw.println("# init player " + p);
                        if (!blocks[p].isEmpty()) pw.print(blocks[p].get(0));
                    }
                    // Кадр 0 у SDK — инициализация; дальше кадр f = ход (f-1)/2 игрока (f-1)%2.
                    // В логе номер кадра пишем как 2*ход + игрок.
                    int nFrames = r.summaries.size();
                    for (int f = 1; f < nFrames; f++) {
                        int p = (f - 1) % 2;
                        int t = (f - 1) / 2;
                        if (t + 1 >= blocks[p].size()) break;
                        pw.println("# frame " + (2 * t + p) + " player " + p);
                        pw.print(blocks[p].get(t + 1));
                        List<String> outs = r.outputs.get(String.valueOf(p));
                        String out = outs != null && f < outs.size() ? outs.get(f) : null;
                        if (out != null) for (String line : out.split("\n")) if (line.trim().length() > 0) pw.println("> " + line.trim());
                        String sum = r.summaries.get(f);
                        if (sum != null) for (String line : sum.split("\n")) if (line.trim().length() > 0) pw.println("# summary " + line.trim());
                    }
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
