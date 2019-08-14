namespace InformedICU.Domain.Projections

module Patient =

    open System
    open InformedICU.Domain.Types

    type Patient =
        {
            Id : string
            HospitalNumber : string
            LastName : string
            FirstName : string
            BirthDate : DateTime Option
            Admissions : Admission list
        }
    and Admission = 
        {   
            No : int
            AdmissionDate : DateTime 
            DischargeDate : DateTime Option
        }

    let empty =
        {
            Id = ""
            HospitalNumber = ""
            LastName = ""
            FirstName = ""
            BirthDate = None
            Admissions = []
        }

    module Projections =

        open InformedICU.Domain
        
        let updatePatient (s : Patient) e = 
            match e with
            | Validated pd ->
                { s with
                    LastName = pd.LastName |> Patient.Name.toString
                    FirstName = pd.FirstName |> Patient.Name.toString
                    BirthDate = pd.BirthDate |> Patient.BirthDate.toDate |> Some
                }
            | Registered rp ->
                { s with
                    HospitalNumber = rp.HospitalNumber |> Patient.HospitalNumber.toString
                }
            | Changed cp ->
                { s with
                    LastName = cp.NewDetails.LastName |> Patient.Name.toString
                    FirstName = cp.NewDetails.FirstName |> Patient.Name.toString
                    BirthDate = cp.NewDetails.BirthDate |> Patient.BirthDate.toDate |> Some
                }
            | Admitted ad ->
                {
                    s with
                        Admissions = 
                            { No = 0; AdmissionDate = ad.AdmissionDate; DischargeDate = None }
                            |> List.singleton
                            |> List.append s.Admissions
                            |> List.mapi (fun i a -> { a with No = i + 1 } )
                }
            | Discharged dd ->
                {
                    s with
                        Admissions =
                            match s.Admissions |> List.rev with
                            | ad::_ ->
                                let adm = { ad with DischargeDate = dd.DischargeDate |> Some }
                                s.Admissions
                                |> List.rev
                                |> List.tail
                                |> List.append [adm]
                                |> List.rev
                            | _ -> s.Admissions
                }
            | _ -> s

        let patientInfo es = es |> List.fold updatePatient empty






