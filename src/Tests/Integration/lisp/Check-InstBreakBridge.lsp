;;; Scenario 2: INSTBREAK, bridge mode, total width 200, bump direction U (up).
;;; Each gap on L1 must come back as a 2-vertex LWPOLYLINE with |bulge| = 1
;;; (a half circle), on the INST layer, spanning exactly the gap.

(defun cf-poly-xs (lay / ss i e out xs)
  (setq out '())
  (setq ss (ssget "X" (list (cons 0 "LWPOLYLINE") (cons 8 lay))))
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq xs '())
        (foreach x e
          (if (= (car x) 10) (setq xs (cons (car (cdr x)) xs)))
        )
        (setq out (cons (vl-sort xs '<) out))
        (setq i (1+ i))
      )
    )
  )
  (vl-sort out '(lambda (a b) (< (car a) (car b))))
)

(cf-report "instbreak-bridge/inst-line-count"
           (= (cf-line-count "INST") 4)
           (strcat "expected 4 (3 L1 pieces + L2), actual " (itoa (cf-line-count "INST"))))

(cf-report "instbreak-bridge/pipe-untouched-count"
           (= (cf-line-count "PIPE") 3)
           (strcat "expected 3, actual " (itoa (cf-line-count "PIPE"))))

(setq cf-p (cf-poly-info "INST"))
(cf-report "instbreak-bridge/bridge-count"
           (= (length cf-p) 2)
           (strcat "expected 2 LWPOLYLINE on INST, actual " (itoa (length cf-p))))

(cf-report "instbreak-bridge/bridge-shape"
           (and (= (length cf-p) 2)
                (apply 'and
                       (mapcar '(lambda (x)
                                  (and (= (car x) 2)
                                       (cf-has-half-bulge (cadr x))))
                               cf-p)))
           (strcat "expected 2 vertices + a |bulge|=1 half circle each, actual " (vl-princ-to-string cf-p)))

(setq cf-xs (cf-poly-xs "INST"))
(cf-report "instbreak-bridge/bridge-spans"
           (and (= (length cf-xs) 2)
                (cf-nums-equal (car cf-xs) '(400.0 600.0) 0.000001)
                (cf-nums-equal (cadr cf-xs) '(700.0 900.0) 0.000001))
           (strcat "expected [400,600][700,900], actual " (vl-princ-to-string cf-xs)))

(cf-report "instbreak-bridge/L1-gap-intervals"
           (cf-seqs-equal (cf-hsegs "INST" 0.0 0.000001)
                          '((0.0 400.0) (600.0 700.0) (900.0 1000.0))
                          0.000001)
           (cf-fmt-seqs (cf-hsegs "INST" 0.0 0.000001)))

(cf-report "instbreak-bridge/L2-same-layer-kept-whole"
           (cf-seqs-equal (cf-vsegs "INST" 200.0 0.000001) '((-300.0 300.0)) 0.000001)
           (cf-fmt-seqs (cf-vsegs "INST" 200.0 0.000001)))

(cf-report "instbreak-bridge/other-layer-untouched"
           (= (cf-line-count "OTHER") 1)
           (strcat "expected 1, actual " (itoa (cf-line-count "OTHER"))))

(cf-finish)
