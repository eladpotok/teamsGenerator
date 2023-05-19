import {  BorderOutlined, CheckOutlined, MinusOutlined } from "@ant-design/icons"
import { Button } from "antd"
import MyIconButton from "./MyIconButton"

function AppCheckBox(props) {
    function checkboxClickedHandler(value) {
        props.onChanged(value) 
    }

    return (
        <>
            {props.value &&  <MyIconButton onClick={()=>{checkboxClickedHandler(false)}} icon={<CheckOutlined />}/> }
            {!props.value &&   <MyIconButton icon={<MinusOutlined />}  onClick={()=>{checkboxClickedHandler(true)}}  />}
        </>
    )

}

export default AppCheckBox