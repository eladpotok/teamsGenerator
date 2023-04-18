import { Button } from 'antd'
import './AppButton.css'

function AppButton(props) {


        return (
            <button  class="button button4" onClick={props.onClick}>
                {props.children}
            </button>
        )

}

export default AppButton