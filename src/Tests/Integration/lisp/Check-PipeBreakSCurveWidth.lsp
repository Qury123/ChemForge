;;; Scenario: PIPEBREAK, S mode, width 100 but S symbol width overridden to 50.
;;; The gap is still cut at 100 wide (P2 breaks into [-200,-150] and [-50,200]) while each S
;;; mark keeps its internal 8:1 proportion, so span = 50 (+/-25) and amplitude = 6.25 (+/-).
;;; Pipe along +Y, across = -X, rise = -Y.

(defun cfp-pt-eq (p q tol)
  (and (cf-approx (car p) (car q) tol) (cf-approx (cadr p) (cadr q) tol))
)

(defun cfp-list-eq (a b tol)
  (if (/= (length a) (length b))
    nil
    (apply 'and (mapcar '(lambda (p q) (cfp-pt-eq p q tol)) a b))
  )
)

(defun cf-spline-fits (lay / ss i e out pts)
  (setq out '())
  (setq ss (ssget "X" (list (cons 0 "SPLINE") (cons 8 lay))))
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq pts '())
        (foreach x e
          (if (= (car x) 11)
            (setq pts (cons (list (car (cdr x)) (cadr (cdr x))) pts))
          )
        )
        (setq out (cons (reverse pts) out))
        (setq i (1+ i))
      )
    )
  )
  out
)

(defun cfp-count-penta (lst penta / n)
  (setq n 0)
  (foreach s lst
    (if (cfp-list-eq s penta 0.000001) (setq n (1+ n)))
  )
  n
)

(defun cfp-penta-present (lst penta)
  (= (cfp-count-penta lst penta) 1)
)

(cf-report "pipebreak-scurve-width/gap-still-100"
           (cf-seqs-equal (cf-vsegs "PIPE" 800.0 0.000001) '((-200.0 -150.0) (-50.0 200.0)) 0.000001)
           (strcat "expected gap [-200,-150][-50,200], actual "
                   (cf-fmt-seqs (cf-vsegs "PIPE" 800.0 0.000001))))

(setq cf-s (cf-spline-fits "PIPE"))
(cf-report "pipebreak-scurve-width/spline-count"
           (= (length cf-s) 2)
           (strcat "expected 2 SPLINE, actual " (itoa (length cf-s))))

(cf-report "pipebreak-scurve-width/span-is-50"
           (and (= (length cf-s) 2)
                (cfp-penta-present cf-s '((825.0 -150.0) (812.5 -156.25) (800.0 -150.0) (787.5 -143.75) (775.0 -150.0))))
           (strcat "expected span +/-25 (S width 50), actual " (vl-princ-to-string cf-s)))

(cf-report "pipebreak-scurve-width/other-end"
           (and (= (length cf-s) 2)
                (cfp-penta-present cf-s '((825.0 -50.0) (812.5 -56.25) (800.0 -50.0) (787.5 -43.75) (775.0 -50.0))))
           (strcat "expected mirror mark at (800,-50), actual " (vl-princ-to-string cf-s)))

(cf-report "pipebreak-scurve-width/no-old-width-mark"
           (and (= (length cf-s) 2)
                (not (cfp-penta-present cf-s '((850.0 -150.0) (825.0 -162.5) (800.0 -150.0) (775.0 -137.5) (750.0 -150.0)))))
           "expected no +/-50 span mark (that would mean S width fell back to 100)")

(cf-report "pipebreak-scurve-width/p3-untouched"
           (= (cf-line-count "PIPE") 4)
           (strcat "expected 4 (P1 + 2 P2 pieces + P3), actual " (itoa (cf-line-count "PIPE"))))

(cf-report "pipebreak-scurve-width/inst-layer-not-selected"
           (= (cf-line-count "INST") 2)
           (strcat "expected 2, actual " (itoa (cf-line-count "INST"))))

(cf-finish)
