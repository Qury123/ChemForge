;;; Scenario 4: PIPEBREAK regression -- single layer PIPE, gap mode, width 100,
;;; break direction H (the horizontal side is the one that gets cut).
;;; P3 (horizontal, y=-100) crosses P2 (vertical, x=800) at (800,-100), so P3 must be
;;; cut into [600,750] + [850,900] and both vertical pipes must stay whole.

(cf-report "pipebreak-regression-h/pipe-line-count"
           (= (cf-line-count "PIPE") 4)
           (strcat "expected 4 (P1 + P2 + 2 P3 pieces), actual " (itoa (cf-line-count "PIPE"))))

(setq cf-h3 (cf-hsegs "PIPE" -100.0 0.000001))
(cf-report "pipebreak-regression-h/P3-gap-intervals"
           (cf-seqs-equal cf-h3 '((600.0 750.0) (850.0 900.0)) 0.000001)
           (strcat "expected [600,750][850,900], actual " (cf-fmt-seqs cf-h3)))

(setq cf-v1 (cf-vsegs "PIPE" 500.0 0.000001))
(cf-report "pipebreak-regression-h/P1-untouched"
           (cf-seqs-equal cf-v1 '((-200.0 200.0)) 0.000001)
           (strcat "expected [-200,200], actual " (cf-fmt-seqs cf-v1)))

(setq cf-v2 (cf-vsegs "PIPE" 800.0 0.000001))
(cf-report "pipebreak-regression-h/P2-untouched"
           (cf-seqs-equal cf-v2 '((-200.0 200.0)) 0.000001)
           (strcat "expected [-200,200], actual " (cf-fmt-seqs cf-v2)))

(cf-report "pipebreak-regression-h/inst-layer-not-selected"
           (= (cf-line-count "INST") 2)
           (strcat "expected 2, actual " (itoa (cf-line-count "INST"))))

(cf-finish)
