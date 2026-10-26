;;; Scenario: PIPEBREAK, S mode, single layer PIPE, width 100, break direction V.
;;; P2 (vertical at x=800) crosses P3 (horizontal at y=-100) -> gap [-150,-50] on P2, so the
;;; two pipe ends are (800,-150) and (800,-50). Each end gets one S mark = ONE spline with
;;; FIVE fit points, wavelength 100 (= width) across the pipe, amplitude +/-12.5 (= width/8)
;;; along the pipe (pipe along +Y, across = -X, rise = -Y).

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

;;; Number of splines in the list whose fit points equal penta.
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

(cf-report "pipebreak-scurve/pipe-line-count"
           (= (cf-line-count "PIPE") 4)
           (strcat "expected 4 (P1 + 2 P2 pieces + P3), actual " (itoa (cf-line-count "PIPE"))))

(setq cf-v2 (cf-vsegs "PIPE" 800.0 0.000001))
(cf-report "pipebreak-scurve/P2-gap-intervals"
           (cf-seqs-equal cf-v2 '((-200.0 -150.0) (-50.0 200.0)) 0.000001)
           (strcat "expected [-200,-150][-50,200], actual " (cf-fmt-seqs cf-v2)))

(setq cf-s (cf-spline-fits "PIPE"))
(cf-report "pipebreak-scurve/spline-count"
           (= (length cf-s) 2)
           (strcat "expected 2 SPLINE on PIPE (2 marks), actual " (itoa (length cf-s))))

(cf-report "pipebreak-scurve/spline-fit-count"
           (and (= (length cf-s) 2)
                (apply 'and (mapcar '(lambda (p) (= (length p) 5)) cf-s)))
           (strcat "expected 5 fit points each, actual " (vl-princ-to-string (mapcar 'length cf-s))))

(cf-report "pipebreak-scurve/mark-at-800-150"
           (and (= (length cf-s) 2)
                (cfp-penta-present cf-s '((850.0 -150.0) (825.0 -162.5) (800.0 -150.0) (775.0 -137.5) (750.0 -150.0))))
           (strcat "expected mark around (800,-150), actual " (vl-princ-to-string cf-s)))

(cf-report "pipebreak-scurve/mark-at-800-50"
           (and (= (length cf-s) 2)
                (cfp-penta-present cf-s '((850.0 -50.0) (825.0 -62.5) (800.0 -50.0) (775.0 -37.5) (750.0 -50.0))))
           (strcat "expected mark around (800,-50), actual " (vl-princ-to-string cf-s)))

(cf-report "pipebreak-scurve/inst-layer-not-selected"
           (= (cf-line-count "INST") 2)
           (strcat "expected 2, actual " (itoa (cf-line-count "INST"))))

(cf-finish)
