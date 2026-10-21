;;; Scenario 3: PIPEBREAK regression -- single layer PIPE, gap mode, width 100,
;;; break direction V (the vertical side is the one that gets cut).
;;; Only PIPE entities take part: P2 (vertical, x=800) crosses P3 (horizontal, y=-100)
;;; at (800,-100), so P2 must be cut into [-200,-150] + [-50,200] and nothing else moves.
;;; Width 100 (half = 50) is deliberate: with the default 200 the split points would land
;;; exactly on P2's end point and the engine would skip the crossing as degenerate.

(cf-report "pipebreak-regression/pipe-line-count"
           (= (cf-line-count "PIPE") 4)
           (strcat "expected 4 (P1 + 2 P2 pieces + P3), actual " (itoa (cf-line-count "PIPE"))))

(setq cf-v1 (cf-vsegs "PIPE" 500.0 0.000001))
(cf-report "pipebreak-regression/P1-untouched"
           (cf-seqs-equal cf-v1 '((-200.0 200.0)) 0.000001)
           (strcat "expected [-200,200], actual " (cf-fmt-seqs cf-v1)))

(setq cf-v2 (cf-vsegs "PIPE" 800.0 0.000001))
(cf-report "pipebreak-regression/P2-gap-intervals"
           (cf-seqs-equal cf-v2 '((-200.0 -150.0) (-50.0 200.0)) 0.000001)
           (strcat "expected [-200,-150][-50,200], actual " (cf-fmt-seqs cf-v2)))

(setq cf-h3 (cf-hsegs "PIPE" -100.0 0.000001))
(cf-report "pipebreak-regression/P3-untouched"
           (cf-seqs-equal cf-h3 '((600.0 900.0)) 0.000001)
           (strcat "expected [600,900], actual " (cf-fmt-seqs cf-h3)))

(cf-report "pipebreak-regression/inst-layer-not-selected"
           (= (cf-line-count "INST") 2)
           (strcat "expected 2, actual " (itoa (cf-line-count "INST"))))

(cf-report "pipebreak-regression/other-layer-not-selected"
           (= (cf-line-count "OTHER") 1)
           (strcat "expected 1, actual " (itoa (cf-line-count "OTHER"))))

(cf-finish)
