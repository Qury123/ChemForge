;;; Scenario: INSTBREAK, S mode, total width 200.
;;; Each gap end gets one S mark = ONE spline with FIVE fit points.
;;; The mark's wavelength runs ACROSS the pipe (200 = gap width, so +/-100), centred on the
;;; end, and the amplitude runs ALONG the pipe (+25 at P1, -25 at P3, i.e. w/8).
;;; Pipe along +X, across = +Y, rise = -X.
;;; Gap [400,600] -> marks at x=400 and x=600; gap [700,900] -> marks at x=700 and x=900.
;;;   ((x,-100) (x-25,-50) (x,0) (x+25,50) (x,100))

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
  (vl-sort out
           '(lambda (a b)
              (if (equal (car (car a)) (car (car b)) 0.000001)
                (< (cadr (car a)) (cadr (car b)))
                (< (car (car a)) (car (car b))))))
)

(defun cf-pt-list-equal (a b tol)
  (if (/= (length a) (length b))
    nil
    (apply 'and
           (mapcar '(lambda (p q)
                      (and (cf-approx (car p) (car q) tol)
                           (cf-approx (cadr p) (cadr q) tol)))
                   a b))
  )
)

(cf-report "instbreak-scurve/inst-line-count"
           (= (cf-line-count "INST") 4)
           (strcat "expected 4 (3 L1 pieces + L2), actual " (itoa (cf-line-count "INST"))))

(cf-report "instbreak-scurve/pipe-untouched-count"
           (= (cf-line-count "PIPE") 3)
           (strcat "expected 3, actual " (itoa (cf-line-count "PIPE"))))

(cf-report "instbreak-scurve/no-bridge-polyline"
           (= (length (cf-poly-info "INST")) 0)
           (strcat "expected 0 LWPOLYLINE on INST, actual " (itoa (length (cf-poly-info "INST")))))

(setq cf-s (cf-spline-fits "INST"))
(cf-report "instbreak-scurve/spline-count"
           (= (length cf-s) 4)
           (strcat "expected 4 SPLINE on INST (1 per gap end, 4 ends), actual " (itoa (length cf-s))))

(cf-report "instbreak-scurve/spline-fit-count"
           (and (= (length cf-s) 4)
                (apply 'and (mapcar '(lambda (p) (= (length p) 5)) cf-s)))
           (strcat "expected 5 fit points each, actual " (vl-princ-to-string (mapcar 'length cf-s))))

(cf-report "instbreak-scurve/mark-400"
           (and (= (length cf-s) 4)
                (cf-pt-list-equal (car cf-s) '((400.0 -100.0) (375.0 -50.0) (400.0 0.0) (425.0 50.0) (400.0 100.0)) 0.000001))
           (strcat "expected ((400,-100)(375,-50)(400,0)(425,50)(400,100)), actual "
                   (vl-princ-to-string (car cf-s))))

(cf-report "instbreak-scurve/mark-600"
           (and (= (length cf-s) 4)
                (cf-pt-list-equal (cadr cf-s) '((600.0 -100.0) (575.0 -50.0) (600.0 0.0) (625.0 50.0) (600.0 100.0)) 0.000001))
           (strcat "expected marks at x=600, actual "
                   (vl-princ-to-string (cadr cf-s))))

(cf-report "instbreak-scurve/mark-700"
           (and (= (length cf-s) 4)
                (cf-pt-list-equal (nth 2 cf-s) '((700.0 -100.0) (675.0 -50.0) (700.0 0.0) (725.0 50.0) (700.0 100.0)) 0.000001))
           (strcat "expected marks at x=700, actual "
                   (vl-princ-to-string (nth 2 cf-s))))

(cf-report "instbreak-scurve/mark-900"
           (and (= (length cf-s) 4)
                (cf-pt-list-equal (nth 3 cf-s) '((900.0 -100.0) (875.0 -50.0) (900.0 0.0) (925.0 50.0) (900.0 100.0)) 0.000001))
           (strcat "expected marks at x=900, actual "
                   (vl-princ-to-string (nth 3 cf-s))))

(cf-finish)
